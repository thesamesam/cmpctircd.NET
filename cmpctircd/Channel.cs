﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmpctircd {
    class Channel {
        public String name;
        // A dictionary of clients in the room (nick => client)
        private Dictionary<String, Client> clients = new Dictionary<string, Client>();

        public void addClient(Client client) {
            if(inhabits(client)) {
                throw new InvalidOperationException("User is already in the room!");
            }
            clients.Add(client.nick, client);

            // Tell everyone we've joined
            send_to_room(client, String.Format(":{0} JOIN :{1}", client.mask(), this.name));
            // TODO: op if size == 1

        }



        /*
         * Useful internals (public methods) 
        */
        public void send_to_room(Client client, String message) {
            // Default: assume send to everyone including the client
            send_to_room(client, message, true);
        }
        public void send_to_room(Client client, String message, Boolean sendSelf) {
            Parallel.ForEach(clients, (iClient) => {
                if (!sendSelf && iClient.Value.Equals(client)) {
                    return;
                }
                iClient.Value.write(message);
            });
        }

        public bool inhabits(Client client) {
            return clients.ContainsValue(client);
        }
        public bool inhabits(String nick) {
            return clients.ContainsKey(nick);
        }
        public void remove(Client client) {
            // TODO: need a PART/QUIT too which this will call
            clients.Remove(client.nick);
        }
        public int size() {
            return clients.Count();
        }

    }
}