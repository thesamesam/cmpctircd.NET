﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace cmpctircd.Controllers {

    /// <summary>
    /// Handles inbound packets for InspIRCd 2.0.
    /// </summary>
    /// <remarks>
    /// For outbound, see <see cref="InspIRCd20"/>.
    /// </remarks>
    [Controller(ListenerType.Server, ServerType.InspIRCd20)]
    public class InspIRCd20Controller : ControllerBase {
        private readonly IRCd ircd;
        private readonly Server server;
        private readonly Log log;

        public InspIRCd20Controller(IRCd ircd, Server server, Log log) {
            this.ircd = ircd ?? throw new ArgumentNullException(nameof(ircd));
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // TODO: three CAPAB packets
        // TODO: Handle BURST, don't process until we get them all? Group the FJOINs
        [Handles("CAPAB")]
        public bool CapabHandler(HandlerArgs args) {
            // TODO: checked if already sent capab?
            if (args.SpacedArgs.Count > 0 && args.SpacedArgs[1] == "START" && server.Listener.GetType() != typeof(SocketConnector)) {
                // Only send if CAPAB START and if inbound connection
                // On outbound, we send straightaway
                server.SendCapab();
            }
            return true;
        }

        [Handles("SERVER")]
        public bool ServerHandler(HandlerArgs args) {
            // TODO: introduce some ServerState magic
            var parts = args.Line.Split(new char[] { ' ' }, 7).ToList();

            // Drop :SID at the beginning (Anope does this, InspIRCd doesn't)
            if(parts[0].StartsWith(":")) {
                parts.RemoveAt(0);
            }

            // Check there are enough parameters
            if (parts.Count() < 6) {
                // TODO: send an error
                return false;
            }

            // Parse out some values
            var hostname = parts[1];
            var password = parts[2];
            var sid      = parts[4];
            var desc     = parts[5].Substring(1);

            // Compare with config
            var foundMatch = server.FindServerConfig(hostname, password);

            if(foundMatch) {
                // Check if the server is already connected
                // [Important that this is after all authentication checks because it has a conditional on whether server is authed]
                try {
                    var foundServer = ircd.GetServerBySID(sid);

                    // If the incoming server is authenticated, disconnect the old one (saves time waiting for ping timeouts, etc)
                    log.Warn($"[SERVER] Ejecting server (SID: {sid}, name: {hostname}) for new connection");
                    foundServer.Disconnect("ERROR: Replaced by a new connection", true);
                } catch (InvalidOperationException) {}

                server.State = ServerState.Auth;
                server.Name = hostname;
                server.SID = sid;
                server.Desc = desc;
                server.Type = ServerType.InspIRCd20;

                // TODO: Change to Info?
                log.Warn($"[SERVER] Got an authed server (SID: {sid}, name: {hostname}, type: {server.Type})");

                // Introduce ourselves
                if(server.Listener.GetType() != typeof(SocketConnector)) {
                    // Only introduce if we're being connected to (i.e. inbound)
                    // This is because for outbound connections, we introduce ourselves first
                    server.SendHandshake();
                }

                // Set the ping cookie to be the SID, so we start pinging them (see CheckTimeout)
                server.PingCookie = server.SID;
            } else {
                log.Warn("[SERVER] Got an unauthed server");
                server.Disconnect("ERROR: Invalid credentials", true);
                return false;
            }
            return true;
        }

        [Handles("UID")]
        public bool UidHandler(HandlerArgs args) {
            var parts = args.Line.Split(new char[] { ' ' }, 12);
            
            // TODO: check parts count
            var last_index = parts.Length - 1;
            var sid_from     = parts[0];
            var user_uuid    = parts[2];
            var timestamp    = parts[3];
            var nick         = parts[4];
            var hostname     = parts[5];
            var display_host = parts[6];
            var ident        = parts[7];
            var ip           = parts[8];
            var signon_time  = parts[9];
            var modes        = parts[10];
            var mode_params  = "";
            var realname     = parts[last_index].Substring(1);
            if(last_index != 11) {
                // If the last index != 11, realname is pushed back by one for an additional mode parameter
                mode_params = parts[11];
            }

            // TODO: needed for FJOIN (???)
            var uid = user_uuid.Substring(3); // Drop the first 2 characters of UUID to make it a UID
            var client = new Client(ircd, server.TcpClient, null, server.Stream, uid, server, true) {
                Nick  = nick,
                Ident = ident,
                SignonTime = Int32.Parse(signon_time),
                RealName = realname,
                OriginServer = server,
                State = ClientState.Auth,
                ResolvingHost = false,
                Listener = server.Listener
                // TODO IP
                // client.IP = System.Net.IPAddress.Parse(ip)
            };

            // TODO modes
            server.Listener.Clients.Add(client);

            ++server.Listener.ClientCount;
            ++server.Listener.AuthClientCount;

            log.Debug($"[SERVER] got new client {nick}");
            return true;
        }

        [Handles("FJOIN")]
        public bool FjoinHandler(HandlerArgs args) {
            // TODO check if the SID is one we know, maybe specify in config? (???)
            var parts     = args.Line.Split(new char[] { ' ' }, 6);
            var channel   = parts[2];
            var userList  = parts[5].Split(new char[] { ' ' });
            foreach(var user in userList) {
                var userParts = user.Split(new char[] { ',' });

                if (userParts.Count() != 2) {
                    continue;
                }

                var modes     = userParts[0];
                var UID       = userParts[1];

                try {
                    Channel chan = null;
                    var client = ircd.GetClientByUUID(UID);
                    try {
                        // Use the channel if it already exists (very unlikely)
                        chan = ircd.ChannelManager.Channels[channel];
                    } catch(KeyNotFoundException) {
                        chan = ircd.ChannelManager.Create(channel);
                    } finally {
                        chan.AddClient(client);
                    }
                    
                } catch(Exception e) {
                    log.Debug($"[SERVER] exception in FJOIN handler! {e.ToString()}");
                }

                
            }
            return true;
        }

        [Handles("QUIT")]
        public bool QuitHandler(HandlerArgs args) {
            return ircd.PacketManager.Handle("QUIT", ircd, args, ListenerType.Client);
        }

        [Handles("SQUIT")]
        public bool SQuitHandler(HandlerArgs args) {
            // TODO: reason?
            log.Info($"Server {server.Name} sent SQUIT; disconnecting");
            server.Disconnect(true);

            return true;
        }

        [Handles("PRIVMSG")]
        public bool PrivMsgHandler(HandlerArgs args) {
            return ircd.PacketManager.Handle("PRIVMSG", ircd, args, ListenerType.Client);
        }

        [Handles("NOTICE")]
        public bool NoticeHandler(HandlerArgs args) {
            return ircd.PacketManager.Handle("NOTICE", ircd, args, ListenerType.Client);
        }

        [Handles("FMODE")]
        public bool FModeHandler(HandlerArgs args) {
            // Trust the server
            args.Force = true;

            // Hack to drop the TS for now
            // TODO: Implement TS
            args.SpacedArgs.RemoveAt(2);

            // Convert all of the UUID args to nicks
            // TODO: May be moved to individual modes
            for (int i = 3; i < args.SpacedArgs.Count(); i++) {
                var arg = args.SpacedArgs[i];

                try {
                    if(ircd.IsUUID(arg)) {
                        args.SpacedArgs[i] = ircd.GetClientByUUID(arg).Nick;
                    }
                } catch(InvalidOperationException) {
                    // We normally check if the user exists in the normal handler
                    // But because MODE could have nicks at any point, the alternative is to check for UUIDs in individual MODEs
                    //throw new IrcErrNoSuchTargetNickException(args.Client, args.SpacedArgs[3]);
                }
            }

            // Call the normal mode with the modified args
            return ircd.PacketManager.Handle("MODE", ircd, args, ListenerType.Client);
        }

        [Handles("SVSNICK")]
        public bool SVSNickHandler(HandlerArgs args) {
            // SVSMODE format: :SID SVSMODE TARGET_UUID NEW_NICK TS
            // NICK    format: NICK NEW_NICK

            var target   = server.IRCd.GetClientByUUID(args.SpacedArgs[1]);
            var new_nick = args.SpacedArgs[2];

            args.Line   = $"NICK {new_nick}";

            return ircd.PacketManager.Handle("NICK", ircd, target, args, ListenerType.Client);
        }
    }
}
