using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace JvtechServeur.Pxe;

/// <summary>
/// Serveur TFTP en lecture seule pour les fichiers de démarrage. Il gère les options de taille de bloc et de fenêtre
/// (RFC 2348 et 7440) que le gestionnaire de démarrage de Windows utilise pour charger Windows PE en une minute environ.
/// Les chemins demandés sont tolérants : majuscules, barres obliques inverses, et recherche par nom de fichier.
/// </summary>
internal sealed class TftpServer : IDisposable
{
    private const ushort ReadRequest = 1, Data = 3, Acknowledge = 4, Error = 5, OptionAck = 6;

    private readonly IPAddress _server;
    private readonly string _root;
    private readonly Action<string> _log;
    private readonly Action<IPAddress, string> _sent;
    private UdpClient? _socket;
    private volatile bool _running;
    private Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="sent">Appelé quand un fichier a été entièrement envoyé à un PC.</param>
    public TftpServer(IPAddress server, string root, Action<string> log, Action<IPAddress, string> sent)
    {
        _server = server;
        _root = Path.GetFullPath(root);
        _log = log;
        _sent = sent;
    }

    public void Start()
    {
        _byName = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _running = true;
        _socket = new UdpClient(new IPEndPoint(_server, 69));
        new Thread(Listen) { IsBackground = true, Name = "TFTP" }.Start();
    }

    private void Listen()
    {
        while (_running)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = _socket!.Receive(ref remote);
                if (data.Length > 4 && Opcode(data) == ReadRequest)
                {
                    var client = remote;
                    new Thread(() => Transfer(data, client)) { IsBackground = true, Name = "TFTP " + client }.Start();
                }
            }
            catch (SocketException) when (!_running) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex) { _log("TFTP : " + ex.Message); }
        }
    }

    private void Transfer(byte[] request, IPEndPoint client)
    {
        var fields = Encoding.ASCII.GetString(request, 2, request.Length - 2).Split('\0');
        var requested = fields[0];
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 2; i + 1 < fields.Length; i += 2)
            if (fields[i].Length > 0) options[fields[i]] = fields[i + 1];

        using var socket = new UdpClient(new IPEndPoint(_server, 0));
        socket.Connect(client);
        socket.Client.ReceiveTimeout = 2000;

        var path = Resolve(requested);
        if (path is null)
        {
            SendError(socket, 1, "Fichier introuvable");
            return;
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var blockSize = 512;
            var window = 1;
            var accepted = new List<(string, string)>();
            if (options.TryGetValue("blksize", out var b) && int.TryParse(b, out var size))
            {
                blockSize = Math.Max(8, Math.Min(size, 1468));
                accepted.Add(("blksize", blockSize.ToString()));
            }
            if (options.TryGetValue("windowsize", out var w) && int.TryParse(w, out var win))
            {
                window = Math.Max(1, Math.Min(win, 64));
                accepted.Add(("windowsize", window.ToString()));
            }
            if (options.ContainsKey("tsize")) accepted.Add(("tsize", file.Length.ToString()));
            if (options.TryGetValue("timeout", out var t)) accepted.Add(("timeout", t));

            if (accepted.Count > 0)
            {
                var oack = new List<byte> { 0, (byte)OptionAck };
                foreach (var (name, value) in accepted)
                {
                    oack.AddRange(Encoding.ASCII.GetBytes(name)); oack.Add(0);
                    oack.AddRange(Encoding.ASCII.GetBytes(value)); oack.Add(0);
                }
                if (!SendAndWait(socket, oack.ToArray(), 0)) return;
            }

            // Numéros de bloc sur 16 bits qui repartent à 0 : boot.wim dépasse largement 65 535 blocs.
            var total = file.Length / blockSize + 1;
            long acked = 0;
            var retries = 0;
            var buffer = new byte[blockSize];
            while (acked < total && _running)
            {
                var last = Math.Min(acked + window, total);
                for (var block = acked + 1; block <= last; block++)
                {
                    file.Position = (block - 1) * blockSize;
                    var read = Fill(file, buffer);
                    var packet = new byte[4 + read];
                    packet[1] = (byte)Data;
                    packet[2] = (byte)((block >> 8) & 0xFF);
                    packet[3] = (byte)(block & 0xFF);
                    Array.Copy(buffer, 0, packet, 4, read);
                    socket.Send(packet, packet.Length);
                }

                var progress = WaitAck(socket, acked, last);
                if (progress > acked)
                {
                    acked = progress;
                    retries = 0;
                }
                else if (++retries > 6)
                {
                    _log($"TFTP : {client.Address} ne répond plus pendant l'envoi de {Path.GetFileName(path)}.");
                    return;
                }
            }
            if (acked >= total) _sent(client.Address, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
        {
            _log($"TFTP : envoi de {requested} à {client.Address} interrompu : {ex.Message}");
        }
    }

    /// <summary>Attend l'accusé de réception le plus avancé de la fenêtre. Renvoie le dernier bloc confirmé.</summary>
    private static long WaitAck(UdpClient socket, long acked, long last)
    {
        var best = acked;
        try
        {
            while (true)
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = socket.Receive(ref remote);
                if (data.Length < 4 || Opcode(data) != Acknowledge) continue;
                var number = (ushort)((data[2] << 8) | data[3]);
                // Retrouve le numéro complet à partir des 16 bits reçus.
                var diff = (ushort)(number - (ushort)(acked & 0xFFFF));
                var block = acked + diff;
                if (block > best && block <= last) best = block;
                if (best == last) return best;
                socket.Client.ReceiveTimeout = 300;
            }
        }
        catch (SocketException)
        {
            return best;
        }
        finally
        {
            socket.Client.ReceiveTimeout = 2000;
        }
    }

    private static bool SendAndWait(UdpClient socket, byte[] packet, ushort expected)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            socket.Send(packet, packet.Length);
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = socket.Receive(ref remote);
                if (data.Length >= 4 && Opcode(data) == Acknowledge && ((data[2] << 8) | data[3]) == expected) return true;
                if (data.Length >= 2 && Opcode(data) == Error) return false;
            }
            catch (SocketException) { }
        }
        return false;
    }

    private static int Fill(Stream stream, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
        return total;
    }

    private string? Resolve(string requested)
    {
        var relative = requested.Replace('/', '\\').TrimStart('\\');
        try
        {
            var full = Path.GetFullPath(Path.Combine(_root, relative));
            if (full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        // Le gestionnaire de démarrage demande parfois \EFI\Microsoft\Boot\... ou \Boot\... : on retrouve le fichier par son nom.
        return _byName.TryGetValue(Path.GetFileName(relative), out var byName) ? byName : null;
    }

    private static ushort Opcode(byte[] data) => (ushort)((data[0] << 8) | data[1]);

    private static void SendError(UdpClient socket, ushort code, string message)
    {
        var packet = new List<byte> { 0, (byte)Error, 0, (byte)code };
        packet.AddRange(Encoding.ASCII.GetBytes(message));
        packet.Add(0);
        try { socket.Send(packet.ToArray(), packet.Count); } catch (SocketException) { }
    }

    public void Dispose()
    {
        _running = false;
        _socket?.Close();
    }
}
