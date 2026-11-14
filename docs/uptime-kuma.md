# Uptime Kuma pour JVTECH Sonde

Uptime Kuma reçoit chaque jour l'état de chaque PC surveillé et prévient JVTECH (e-mail, téléphone) quand un point
est rouge ou quand un PC ne donne plus de nouvelles. Installation proposée : un conteneur dans le Proxmox de JVTECH,
publié sur Internet par un **Cloudflare Tunnel** (gratuit, HTTPS, aucun port ouvert sur l'UDM Pro).

Prérequis : un nom de domaine dont les DNS sont gérés par Cloudflare (compte gratuit), par exemple `kuma.jvtech.fr`.

## 1. Le conteneur dans Proxmox

1. Proxmox > *Create CT* : Debian 12, 1 cœur, 1 Go de RAM, 8 Go de disque, réseau sur le VLAN des serveurs,
   « Unprivileged container » coché, option *Nesting* cochée (nécessaire pour Docker).
2. Dans le conteneur :
   ```
   apt update && apt -y upgrade
   apt -y install docker.io docker-compose-plugin curl
   mkdir -p /opt/kuma && cd /opt/kuma
   ```
3. Créer `/opt/kuma/compose.yaml` :
   ```yaml
   services:
     uptime-kuma:
       image: louislam/uptime-kuma:2
       restart: unless-stopped
       volumes:
         - ./data:/app/data
       # Pas de port publié sur le réseau : seul le tunnel Cloudflare y accède.
     cloudflared:
       image: cloudflare/cloudflared:latest
       restart: unless-stopped
       command: tunnel run
       environment:
         - TUNNEL_TOKEN=${TUNNEL_TOKEN}
   ```
4. Créer le tunnel (étape 2), mettre son jeton dans `/opt/kuma/.env` (`TUNNEL_TOKEN=…`, fichier lisible par root
   seulement : `chmod 600 .env`), puis `docker compose up -d`.

## 2. Le tunnel Cloudflare

1. Tableau de bord Cloudflare > *Zero Trust* > *Networks* > *Tunnels* > *Create a tunnel* (type Cloudflared),
   nom « jvtech-kuma ». Copier le **jeton** affiché (dans la commande d'installation) vers `.env`.
2. *Public hostname* : sous-domaine `kuma`, domaine `jvtech.fr`, service `HTTP`, URL `uptime-kuma:3001`.
3. **Protéger le tableau de bord** : *Zero Trust* > *Access* > *Applications* > *Add* > *Self-hosted* :
   - application 1 « Kuma » sur `kuma.jvtech.fr`, politique *Allow* pour ton adresse e-mail (code reçu par e-mail) ;
   - application 2 « Kuma push » sur `kuma.jvtech.fr/api/push/*`, politique *Bypass* pour *Everyone* : les sondes
     doivent pouvoir envoyer sans se connecter. Chaque adresse Push contient un jeton propre au PC.
4. Ouvrir `https://kuma.jvtech.fr`, créer le compte administrateur de Kuma (mot de passe long, puis
   *Paramètres* > *Sécurité* > double authentification).

## 3. Les notifications

*Paramètres* > *Notifications* > *Configurer une notification* : e-mail (SMTP de la messagerie JVTECH) et, pour le
téléphone, l'application ntfy ou Telegram. Cocher « Activé par défaut » pour qu'elles s'appliquent à chaque nouveau moniteur.

## 4. Un moniteur par PC

Voir [sonde.md](sonde.md), « Mise en place sur un PC » : type **Push**, intervalle **90000 s** (25 h), puis copier
l'adresse Push dans `sonde.ini`.

## Sauvegarde et mises à jour

- Sauvegarde : le dossier `/opt/kuma/data` (inclus dans la sauvegarde Proxmox du conteneur).
- Mise à jour : `cd /opt/kuma && docker compose pull && docker compose up -d` une fois par mois.
- Surveiller Kuma lui-même : s'il tombe, plus aucune alerte n'arrive. Un moniteur externe gratuit (par exemple
  le contrôle d'état d'un service en ligne gratuit sur `https://kuma.jvtech.fr`) prévient dans ce cas.
