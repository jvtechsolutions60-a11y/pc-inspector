using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace JvtechServeur.Pxe;

/// <summary>
/// Serveur « proxyDHCP » : la box garde son rôle et donne les adresses IP ; ce serveur répond seulement aux PC qui
/// démarrent par le réseau (PXE) pour leur dire où télécharger le fichier de démarrage (bootmgfw.efi, signé par
/// Microsoft, donc compatible Secure Boot). Il n'attribue aucune adresse et ne gêne pas les autres appareils.
/// </summary>
internal sealed class ProxyDhcp : IDisposable
{
    public const string BootFile = "bootmgfw.efi";

    private const byte Discover = 1, Offer = 2, Request = 3, Ack = 5;
    private static readonly byte[] Magic = { 99, 130, 83, 99 };

    private readonly IPAddress _server;
    private readonly Func<string, bool> _accept;
    private readonly Action<string> _log;
    private readonly Action<string, string> _booting;
    private readonly List<UdpClient> _sockets = new();
    private volatile bool _running;

    /// <param name="accept">Le PC (adresse MAC) a-t-il le droit de démarrer sur le serveur ?</param>
    /// <param name="booting">Appelé avec l'adresse MAC et le type de PC quand un démarrage réseau est accordé.</param>
    public ProxyDhcp(IPAddress server, Func<string, bool> accept, Action<string> log, Action<string, string> booting)
    {
        _server = server;
        _accept = accept;
        _log = log;
        _booting = booting;
    }

    public void Start()
    {
        _running = true;
        foreach (var port in new[] { 67, 4011 })
        {
            var socket = new UdpClient();
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            // Lié à l'adresse du serveur : les réponses partent par la bonne carte réseau.
            socket.Client.Bind(new IPEndPoint(_server, port));
            _sockets.Add(socket);
            var thread = new Thread(() => Listen(socket, port)) { IsBackground = true, Name = "proxyDHCP " + port };
            thread.Start();
        }
    }

    private void Listen(UdpClient socket, int port)
    {
        while (_running)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = socket.Receive(ref remote);
                Handle(socket, port, data, remote);
            }
            catch (SocketException) when (!_running) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex)
            {
                _log($"proxyDHCP {port} : {ex.Message}");
            }
        }
    }

    private void Handle(UdpClient socket, int port, byte[] data, IPEndPoint remote)
    {
        if (data.Length < 240 || data[0] != 1 || !data.Skip(236).Take(4).SequenceEqual(Magic)) return;
        var options = ParseOptions(data);
        if (!options.TryGetValue(60, out var vendor) || !Encoding.ASCII.GetString(vendor).StartsWith("PXEClient")) return;
        if (!options.TryGetValue(53, out var type) || type.Length != 1) return;
        // Sur le port 67 on répond à la découverte ; sur le port 4011, à la demande qui suit chez certains PC.
        var reply = (port, type[0]) switch
        {
            (67, Discover) => Offer,
            (4011, Request) => Ack,
            _ => (byte)0,
        };
        if (reply == 0) return;

        var mac = string.Join(":", data.Skip(28).Take(Math.Min((int)data[2], 16)).Select(b => b.ToString("X2")));
        var arch = options.TryGetValue(93, out var a) && a.Length >= 2 ? (a[0] << 8) | a[1] : -1;
        // 7 et 9 : UEFI 64 bits. 0 : ancien BIOS, que Windows 11 ne prend pas en charge.
        if (arch is not (7 or 9))
        {
            if (reply == Offer) _log($"{mac} : démarrage réseau en mode {(arch == 0 ? "BIOS ancien" : "type " + arch)}, ignoré. Passer le PC en UEFI dans le BIOS.");
            return;
        }
        if (!_accept(mac))
        {
            if (reply == Offer) _log($"{mac} : démarrage réseau refusé (cliquer sur « Accepter les PC » pour l'autoriser).");
            return;
        }

        var packet = Build(data, reply, options);
        if (reply == Offer)
        {
            socket.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 68));
            _booting(mac, "UEFI 64 bits");
        }
        else
        {
            socket.Send(packet, packet.Length, remote);
        }
    }

    private byte[] Build(byte[] request, byte type, Dictionary<byte, byte[]> requestOptions)
    {
        var packet = new byte[236];
        packet[0] = 2;                                   // réponse
        packet[1] = request[1];                          // type de matériel
        packet[2] = request[2];                          // longueur de l'adresse MAC
        Array.Copy(request, 4, packet, 4, 4);            // identifiant de l'échange
        Array.Copy(request, 10, packet, 10, 2);          // drapeaux (diffusion)
        Array.Copy(request, 12, packet, 12, 4);          // adresse du client s'il en a une
        Array.Copy(_server.GetAddressBytes(), 0, packet, 20, 4); // serveur suivant : ce PC (TFTP)
        Array.Copy(request, 24, packet, 24, 4);          // relais
        Array.Copy(request, 28, packet, 28, 16);         // adresse MAC
        var name = Encoding.ASCII.GetBytes(_server.ToString());
        Array.Copy(name, 0, packet, 44, Math.Min(name.Length, 63));
        var file = Encoding.ASCII.GetBytes(BootFile);
        Array.Copy(file, 0, packet, 108, file.Length);

        var options = new List<byte>();
        options.AddRange(Magic);
        void Add(byte code, byte[] value)
        {
            options.Add(code);
            options.Add((byte)value.Length);
            options.AddRange(value);
        }
        Add(53, new[] { type });
        Add(54, _server.GetAddressBytes());
        Add(60, Encoding.ASCII.GetBytes("PXEClient"));
        Add(66, Encoding.ASCII.GetBytes(_server.ToString()));
        Add(67, file);
        // Contrôle de la découverte PXE : utiliser directement le fichier donné ici, sans autre serveur de démarrage.
        Add(43, new byte[] { 6, 1, 8, 255 });
        if (requestOptions.TryGetValue(97, out var uuid)) Add(97, uuid);
        options.Add(255);
        return packet.Concat(options).ToArray();
    }

    private static Dictionary<byte, byte[]> ParseOptions(byte[] data)
    {
        var options = new Dictionary<byte, byte[]>();
        var i = 240;
        while (i < data.Length)
        {
            var code = data[i++];
            if (code == 0) continue;
            if (code == 255 || i >= data.Length) break;
            var length = data[i++];
            if (i + length > data.Length) break;
            options[code] = data.Skip(i).Take(length).ToArray();
            i += length;
        }
        return options;
    }

    public void Dispose()
    {
        _running = false;
        foreach (var socket in _sockets) socket.Close();
        _sockets.Clear();
    }
}
