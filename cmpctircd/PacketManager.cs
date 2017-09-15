﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmpctircd {
    public class PacketManager {
        private IRCd ircd;
        public Dictionary<String, List<HandlerInfo>> handlers = new Dictionary<string, List<HandlerInfo>>();

        public struct HandlerInfo {
            public string Packet;
            public Func<HandlerArgs, Boolean> Handler;
            public ListenerType Type;
        }

        public PacketManager(IRCd ircd) {
            this.ircd = ircd;
        }

        public bool Register(HandlerInfo info) {
            ircd.Log.Debug("Registering packet: " + info.Packet);
            if (handlers.ContainsKey(info.Packet.ToUpper())) {
                // Already a handler for this packet so add it to the list
                handlers[info.Packet].Add(info);
            } else {
                // No handlers for this packet yet, so create the List
                var list = new List<HandlerInfo>();
                list.Add(info);
                handlers.Add(info.Packet.ToUpper(), list);
            }
            return true;
        }

        public bool Register(string packet, Func<HandlerArgs, Boolean> handler, ListenerType type = ListenerType.Client) {
            // Legacy function, defaults to registering ListenerType.Client packets
            return Register(new HandlerInfo {
                Packet  = packet,
                Handler = handler,
                Type    = type
            });
        }


        public bool FindHandler(String packet, HandlerArgs args, ListenerType type)
        {
            List<String> registrationCommands = new List<String>();
            List<String> idleCommands = new List<String>();

            var client = args.Client;
            if(client != null) {

                registrationCommands.Add("USER");
                registrationCommands.Add("NICK");
                registrationCommands.Add("CAP"); // TODO: NOT YET IMPLEMENTED
                registrationCommands.Add("PONG");
                idleCommands.Add("PING");
                idleCommands.Add("PONG");
                idleCommands.Add("WHOIS");
                idleCommands.Add("WHO");
                idleCommands.Add("NAMES");
                idleCommands.Add("AWAY");

                try {
                    // Restrict the commands which non-registered (i.e. pre PONG, pre USER/NICK) users can execute
                    if((client.State.Equals(ClientState.PreAuth) || (args.IRCd.Config.ResolveHostnames && args.Client.ResolvingHost)) && !registrationCommands.Contains(packet.ToUpper())) {
                        throw new IrcErrNotRegisteredException(client);
                    }

                    // Only certain commands should reset the idle clock
                    if(!idleCommands.Contains(packet.ToUpper())) {
                        client.IdleTime = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    }
                } catch(Exception e) {
                    ircd.Log.Debug("Exception: " + e.ToString());
                }
            } else {
                // Server
                // TODO add server specific checks
            }

            var functions = FindHandlers(packet, type);
            if(functions.Count() > 0) {
                foreach(var record in functions) {
                    // Invoke all of the handlers for the command
                    record.Handler.Invoke(args);
                }
            } else {
                ircd.Log.Debug("No handler for " + packet.ToUpper());
                if(client != null)
                    throw new IrcErrUnknownCommandException(client, packet.ToUpper());
            }
            ircd.Log.Debug("Handler for " + packet.ToUpper() + " executed");
            return true;
        }


        private List<HandlerInfo> FindHandlers(string name, ListenerType type) {
            var functions = new List<HandlerInfo>();
            name = name.ToUpper();
            if (handlers.ContainsKey(name)) {
                functions.AddRange(handlers[name].Where(record => record.Type == type));
            }
            return functions;
        }

        public bool Load() {
            var classes = AppDomain.CurrentDomain.GetAssemblies()
                                   .SelectMany(t => t.GetTypes())
                                   .Where(
                                       t => t.IsClass &&
                                            t.Namespace == "cmpctircd.Packets" &&
                                            t.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), true).Count() == 0
                                    );
            foreach(Type className in classes) {
                Activator.CreateInstance(Type.GetType(className.ToString()), ircd);
            }
            return true;
        }
    }
}
