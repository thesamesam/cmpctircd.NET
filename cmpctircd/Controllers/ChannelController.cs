﻿using System;
using System.Collections.Generic;
using System.Linq;

using cmpctircd.Modes;

namespace cmpctircd.Controllers {
    [Controller(ListenerType.Client)]
    public class ChannelController : ControllerBase {
        private readonly IRCd ircd;
        private readonly Client client;

        public ChannelController(IRCd ircd, Client client) {
            this.ircd = ircd ?? throw new ArgumentNullException(nameof(ircd));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Handles the LIST command sent by clients, which writes back a list of channels.
        /// 
        /// Defined in <a href="https://tools.ietf.org/html/rfc1459#section-4.2.6">section 4.2.6</a> of <a href="https://tools.ietf.org/html/rfc1459">RFC 1459</a>.
        /// </summary>
        /// <param name="args">The arguments object.</param>
        /// <returns>TRUE to indicate success.</returns>
        [Handles("LIST")]
        public bool ListHandler(HandlerArgs args) {
            var manager = ircd.ChannelManager;
            var selections = args.SpacedArgs.ElementAtOrDefault(1)?.Split(',');

            client.Write($":{ircd.Host} {IrcNumeric.RPL_LISTSTART.Printable()} {client.Nick} Channel :Users  Name");
            (selections ?? manager.Channels.Keys.AsEnumerable())
                .Select(n => manager.Channels[n])
                .ForEach(c => {
                    if(c != null) {
                        var size = c.Size;

                        if (c.Clients.Values.Contains(client)) {
                            // Exclude users with invisible mode (+i) set if we're not in the same room
                            size -= c.Clients.Values.Where(member => member.Modes["i"].Enabled).Count();
                        }
                        client.Write($":{ircd.Host} {IrcNumeric.RPL_LIST.Printable()} {client.Nick} {c.Name} {c.Size} :{c.Topic.TopicText}");
                    }
                });
            client.Write($":{ircd.Host} {IrcNumeric.RPL_LISTEND.Printable()} {client.Nick} :End of /LIST");
            return true;
        }

        [Handles("INVITE")]
        public bool InviteHandler(HandlerArgs args) {
            Channel channel;
            Client targetClient;

            if (args.SpacedArgs.Count <= 2)
                throw new IrcErrNotEnoughParametersException(client, "INVITE");

            if(ircd.ChannelManager.Exists(args.SpacedArgs[2])) {
                channel = ircd.ChannelManager[args.SpacedArgs[2]];
            } else {
                throw new IrcErrNoSuchTargetNickException(client, args.SpacedArgs[2]);
            }

            try {
                targetClient = ircd.GetClientByNick(args.SpacedArgs[1]);
            } catch(InvalidOperationException) {
                throw new IrcErrNoSuchTargetNickException(client, args.SpacedArgs[1]);
            }

            if(!channel.Clients.ContainsKey(client.Nick)) {
                throw new IrcErrNotOnChannelException(client, channel.Name);
            }

            if (channel.Clients.ContainsKey(targetClient.Nick)) {
                throw new IrcErrUserOnChannelException(client, targetClient.Nick, channel.Name);
            }

            ChannelPrivilege sourcePrivs = channel.Privileges.GetOrAdd(client, ChannelPrivilege.Normal);
            if(sourcePrivs.CompareTo(ChannelPrivilege.Op) < 0) {
                throw new IrcErrChanOpPrivsNeededException(client, channel.Name);
            }

            channel.SendToRoom(client, $":{ircd.Host} {IrcNumeric.RPL_INVITING.Printable()} {client.Nick} {targetClient.Nick} :{channel.Name}", true);
            targetClient.Write($":{client.Mask} INVITE {targetClient.Nick} :{channel.Name}");
            targetClient.Invites.Add(channel);
            return true;
        }

        [Handles("KICK")]
        public bool KickHandler(HandlerArgs args) {
            Client targetClient;
            Channel channel;
            String target;
            String message;


            if(args.SpacedArgs.Count <= 2) {
                throw new IrcErrNotEnoughParametersException(client, "KICK");
            }

            if(args.SpacedArgs.Count >= 4) {
                message = args.Trailer;
            } else {
                message = client.Nick;
            }

            target  = args.SpacedArgs[2];

            if (ircd.ChannelManager.Exists(args.SpacedArgs[1])) {
                channel = ircd.ChannelManager[args.SpacedArgs[1]];
            } else {
                throw new IrcErrNoSuchTargetChannelException(client, args.SpacedArgs[1]);
            }

            try {
                targetClient = ircd.GetClientByNick(target);
            } catch (InvalidOperationException) {
                throw new IrcErrNoSuchTargetNickException(client, target);
            }

            ChannelPrivilege sourcePrivs = channel.Privileges.GetOrAdd(client, ChannelPrivilege.Normal);
            if(sourcePrivs.CompareTo(ChannelPrivilege.Op) < 0) {
                throw new IrcErrChanOpPrivsNeededException(client, channel.Name);
            }

            channel.SendToRoom(client, $":{client.Mask} KICK {channel.Name} {targetClient.Nick} :{message}", true);
            channel.Remove(targetClient, true);
            return true;
        }

        [Handles("TOPIC")]
        public bool TopicHandler(HandlerArgs args) {
            Topic topic;
            if(args.SpacedArgs.Count == 0) {
                throw new IrcErrNotEnoughParametersException(client, "TOPIC");
            } else {
                string target = args.SpacedArgs[1];
                if(!ircd.ChannelManager.Channels.ContainsKey(target))
                    throw new IrcErrNoSuchTargetNickException(client, target);
                topic = ircd.ChannelManager[target].Topic;
                if(args.Trailer != null)
                    topic.SetTopic(client, target, args.Trailer);
                else
                    topic.GetTopic(client, target);
                return true;
            }
        }

        [Handles("JOIN")]
        public bool JoinHandler(HandlerArgs args) {
            String[] splitCommaLine;
            Channel channel;
            Topic topic;


            if(args.SpacedArgs.Count == 0 && String.IsNullOrWhiteSpace(args.Trailer)) {
                throw new IrcErrNotEnoughParametersException(client, "JOIN");
            }

            try {
                // Message of format: JOIN #x,#y,#z
                splitCommaLine = args.SpacedArgs[1].Split(new char[] { ','});
            } catch(ArgumentOutOfRangeException) {
                // Message of format: JOIN :#x,#y,#z
                splitCommaLine = args.Trailer.Split(new char[] { ',' });
            }

            for(int i = 0; i < splitCommaLine.Length; i++) {
                if((i + 1) > ircd.MaxTargets) break;

                string channel_name = splitCommaLine[i];
                // Some bots will try to send ':' with the channel, remove this
                channel_name = channel_name.StartsWith(":") ? channel_name.Substring(1) : channel_name;
                // We don't need to check for commas because the split handled that.
                // TODO: Do check for proper initializing char, and check for BEL and space.
                if (!ChannelManager.IsValid(channel_name)) {
                    throw new IrcErrNoSuchTargetChannelException(client, channel_name);
                }
                // Get the channel object, creating it if it doesn't already exist
                // TODO: only applicable error is ERR_NEEDMOREPARAMS for now, more for limits/bans/invites
                if (ircd.ChannelManager.Exists(channel_name)) {
                    channel = ircd.ChannelManager[channel_name];
                } else {
                    channel = ircd.ChannelManager.Create(channel_name);
                }

                try {
                    channel.Modes["i"].GetValue();
                    if (!client.Invites.Contains(channel)) {
                        throw new IrcErrInviteOnlyChanException(client, channel.Name);
                    }
                } catch (IrcModeNotEnabledException) {}

                try {
                    channel.Modes["z"].GetValue();
                    if (!client.Modes["z"].Enabled) {
                        throw new IrcErrSecureOnlyChanException(client, channel.Name);
                    }
                } catch(IrcModeNotEnabledException) {}

                try {
                    channel.Modes["O"].GetValue();
                    if (!client.Modes["o"].Enabled) {
                        throw new IrcErrOperOnlyException(client, channel.Name);
                    }
                } catch(IrcModeNotEnabledException) {}

                channel.AddClient(client);
                client.Invites.Remove(channel);
                topic = channel.Topic;
                topic.GetTopic(client, channel_name, true);
            }

            return true;
        }

        [Handles("PRIVMSG")]
        public bool PrivMsgHandler(HandlerArgs args) {
            Client targetClient = null;

            String target;
            String message;

            // Catch both no parameter and whitespace
            if (args.SpacedArgs.Count < 2 || String.IsNullOrWhiteSpace(args.SpacedArgs[1])) {
                throw new IrcErrNoRecipientException(client, "PRIVMSG");
            }

            target = args.SpacedArgs[1];

            if (!target.StartsWith("#")) {
                try {
                    targetClient = ircd.IsUUID(target) ? ircd.GetClientByUUID(target) : ircd.GetClientByNick(target);
                } catch(InvalidOperationException) {
                    throw new IrcErrNoSuchTargetNickException(client, target);
                }
            }

            // Format:
            // PRIVMSG target (:)message
            // PRIVMSG target :message with spaces
            if (String.IsNullOrEmpty(args.Trailer) && args.SpacedArgs.Count < 3 && String.IsNullOrEmpty(args.SpacedArgs[2])) {
                throw new IrcErrNoTextToSendException(client);
            }

            message = String.IsNullOrEmpty(args.Trailer) ? args.SpacedArgs[2] : args.Trailer;

            if (target.StartsWith("#")) {
                // PRIVMSG a channel
                bool NoExternal = false;
                bool moderated = false;
                if(ircd.ChannelManager.Exists(target)) {
                    Channel channel = ircd.ChannelManager[target];
                    try {
                        channel.Modes["n"].GetValue();
                        NoExternal = true;
                    } catch (IrcModeNotEnabledException) {}
                    if(!channel.Inhabits(client) && NoExternal) {
                        throw new IrcErrNotOnChannelException(client, channel.Name);
                    } else {
                        // Check the bans
                        var userRank = channel.Status(client);
                        if (channel.Modes["b"].Has(client) && userRank.CompareTo(ChannelPrivilege.Op) < 0) {
                            throw new IrcErrCannotSendToChanException(client, channel.Name, "Cannot send to channel (You're banned)");
                        }
                        // Don't send PRIVMSG if it's a moderated channel and the client isn't at least voice
                        try {
                            channel.Modes["m"].GetValue();
                            moderated = true;
                        } catch (IrcModeNotEnabledException) {}

                        if (moderated) {
                            if (!channel.Inhabits(client)) {
                                return false;
                            } else if (channel.Inhabits(client)) {
                                // If the user isn't voiced, send ERR_CANNOTSENDTOCHAN
                                if (userRank.CompareTo(ChannelPrivilege.Voice) < 0) {
                                    throw new IrcErrCannotSendToChanException(client, channel.Name, "You need voice (+v)");
                                }
                            }
                        }
                        channel.SendToRoom(client, String.Format(":{0} PRIVMSG {1} :{2}", client.Mask, channel.Name, message), false);
                    }
                } else {
                    throw new IrcErrNoSuchTargetNickException(client, target);
                }
            } else if(targetClient != null) {
                if(!String.IsNullOrWhiteSpace(targetClient.AwayMessage)) {
                    // If the target client (recipient) is away, warn the person (source) sending the message to them.
                    client.Write($":{ircd.Host} {IrcNumeric.RPL_AWAY.Printable()} {client.Nick} {target} :{targetClient.AwayMessage}");
                }
                targetClient.Write(String.Format(":{0} PRIVMSG {1} :{2}", client.Mask, target, message));
            }
            return true;
        }

        [Handles("NOTICE")]
        public bool NoticeHandler(HandlerArgs args) {
            Client targetClient = null;

            String target;
            String message;
            String fmtMessage;

            // Check the target has been sent
            if (args.SpacedArgs.Count >= 1) {
                // Check the target exists
                target = args.SpacedArgs[1];
                if(target.StartsWith("#")) {
                    // The target is a channel
                    if(!ircd.ChannelManager.Exists(target)) {
                        throw new IrcErrNoSuchTargetNickException(client, target);
                    }
                } else {
                    // The target is a user
                    try {
                        targetClient = ircd.IsUUID(target) ? ircd.GetClientByUUID(target) : ircd.GetClientByNick(target);
                    } catch (InvalidOperationException) {
                        throw new IrcErrNoSuchTargetNickException(client, target);
                    }
                }

                if (String.IsNullOrEmpty(args.Trailer)) {
                    // Client has provided a target but no message
                    throw new IrcErrNoTextToSendException(client);
                }

                message    = args.Trailer;
                fmtMessage = String.Format(":{0} NOTICE {1} :{2}", client.Mask, target, message);
                if (target.StartsWith("#")) {
                    bool NoExternal = false;
                    bool moderated = false;
                    Channel channel = ircd.ChannelManager[target];
                    try {
                        channel.Modes["n"].GetValue();
                        NoExternal = true;
                    } catch (IrcModeNotEnabledException) {}
                    if(!channel.Inhabits(client) && NoExternal) {
                        throw new IrcErrNotOnChannelException(client, channel.Name);
                    }
                    // Don't send NOTICE or reply if it's a moderated channel
                    try {
                        channel.Modes["m"].GetValue();
                        moderated = true;
                    } catch (IrcModeNotEnabledException) {}

                    if (moderated) {
                        if (!channel.Inhabits(client)) {
                            return false;
                        } else if (channel.Inhabits(client)) {
                            var userRank = channel.Status(client);
                            // If the user isn't voiced, do nothing
                            if (userRank.CompareTo(ChannelPrivilege.Voice) < 0) {
                                return false;
                            }
                        }
                    }
                    channel.SendToRoom(client, fmtMessage, false);
                    return true;
                } else {
                    targetClient.Write(fmtMessage);
                    return true;
                }

            }
            // XXX: RFC states there should never be a response to NOTICE, but Unreal and others send a No Such Target error message. We will follow them for now.
            return true;
        }

        [Handles("PART")]
        public bool PartHandler(HandlerArgs args) {
            String rawLine = args.Line;
            String[] splitLine = rawLine.Split(new string[] { ":" }, 2, StringSplitOptions.None);
            String[] splitLineSpace = rawLine.Split(new string[] { " " }, 3, StringSplitOptions.None);

            String channel;
            String reason;

            try {
                channel = splitLineSpace[1];
            } catch(IndexOutOfRangeException) {
                throw new IrcErrNotEnoughParametersException(client, "PART");
            }

            try {
                reason = splitLine[1];
            } catch(IndexOutOfRangeException) {
                reason = "Leaving";
            }
            // Some bots will try to send ':' with the channel, remove this
            channel = channel.StartsWith(":") ? channel.Substring(1) : channel;
            // Does the channel exist?
            if(!ircd.ChannelManager.Exists(channel)) {
                throw new IrcErrNoSuchTargetChannelException(client, channel);
            }

            // Are we in the channel?
            Channel channelObj = ircd.ChannelManager[channel];
            if(!channelObj.Inhabits(client)) {
                throw new IrcErrNotOnChannelException(client, channel);
            }

            channelObj.Part(client, reason);
            return true;
        }

        [Handles("WHO")]
        public Boolean WhoHandler(HandlerArgs args) {
            String target;
            Channel targetChannel;

            try {
                target = args.SpacedArgs[1];
            } catch(ArgumentOutOfRangeException) {
                throw new IrcErrNotEnoughParametersException(client, "WHO");
            }

            if(target.StartsWith("#")) {
                // The target is a channel
                try {
                    targetChannel = ircd.ChannelManager.Channels[target];
                } catch(KeyNotFoundException) {
                    throw new IrcErrNoSuchTargetNickException(client, target);
                }

                foreach(var client in targetChannel.Clients) {
                    // TODO: Also for when we have links (:0 is hopcount)
                    var userPriv = targetChannel.Status(client.Value);
                    var userSymbol = targetChannel.GetUserSymbol(userPriv);
                    var ircopSymbol = client.Value.Modes["o"].Enabled ? "*" : "";
                    var hopCount = 0;

                    var away = "";
                    if(String.IsNullOrEmpty(client.Value.AwayMessage)) {
                        away = "H"; // "Here"
                    } else {
                        away = "G"; // "Gone"
                    }
                    this.client.Write($":{ircd.Host} {IrcNumeric.RPL_WHOREPLY.Printable()} {this.client.Nick} {target} {client.Value.Ident} {client.Value.GetHost()} {ircd.Host} {client.Value.Nick} {away}{ircopSymbol}{userSymbol} :{hopCount} {client.Value.RealName}");
                }
                client.Write($":{ircd.Host} {IrcNumeric.RPL_ENDOFWHO.Printable()} {client.Nick} {target} :End of /WHO list.");
            } else {
                // The target is a user
                // See Queries.cs for the user-WHO implementation
            };
            return true;
        }

        [Handles("NAMES")]
        public Boolean NamesHandler(HandlerArgs args) {
            String[] splitLineSpace = args.Line.Split(new string[] { " " }, 3, StringSplitOptions.None);
            String [] splitCommaLine;

            try {
                splitCommaLine = splitLineSpace[1].Split(new char[] { ','});
            } catch(IndexOutOfRangeException) {
                throw new IrcErrNotEnoughParametersException(client, "NAMES");
            }

            for(int i = 0; i < splitCommaLine.Length; i++) {
                if((i + 1) > ircd.MaxTargets) break;

                var channel_name = splitCommaLine[i];
                if (!ChannelManager.IsValid(channel_name)) {
                    throw new IrcErrNoSuchTargetChannelException(client, channel_name);
                }

                if(ircd.ChannelManager.Exists(channel_name)) {
                    Channel targetChannel = ircd.ChannelManager[channel_name];
                    foreach(var client in targetChannel.Clients) {
                        var userPriv = targetChannel.Status(client.Value);
                        var userSymbol = targetChannel.GetUserSymbol(userPriv);
                        this.client.Write($":{ircd.Host} {IrcNumeric.RPL_NAMREPLY.Printable()} {this.client.Nick} = {channel_name} :{userSymbol}{client.Value.Nick}");
                    }
                    client.Write($":{ircd.Host} {IrcNumeric.RPL_ENDOFNAMES.Printable()} {client.Nick} {channel_name} :End of /NAMES list.");
                }
            }

            return true;
        }

        [Handles("MODE")]
        public bool ModeHandler(HandlerArgs args) {
            // This handler is for Channel requests (i.e. where the target begins with a # or &)
            // TODO: update with channel validation logic (in ChannelManager?)
            string target;
            Channel channel;

            try {
                target = args.SpacedArgs[1];
            } catch(ArgumentOutOfRangeException) {
                throw new IrcErrNotEnoughParametersException(client, "MODE");
            }

            if(!target.StartsWith("#") && !target.StartsWith("&")) {
                return false;
            }

            if(ircd.ChannelManager.Exists(target)) {
                channel = ircd.ChannelManager[target];
            } else {
                throw new IrcErrNoSuchTargetChannelException(client, target);
            }

            if(args.SpacedArgs.Count == 2) {
                // This is a request for MODE (e.g. MODE #cmpctircd)
                string[] channelModes = channel.GetModeStrings("+");
                string characters = channelModes[0];
                string argsSet = channelModes[1];
                if(!String.IsNullOrWhiteSpace(argsSet)) {
                    client.Write($":{ircd.Host} {IrcNumeric.RPL_CHANNELMODEIS.Printable()} {client.Nick} {channel.Name} {characters} {argsSet}");
                } else {
                    client.Write($":{ircd.Host} {IrcNumeric.RPL_CHANNELMODEIS.Printable()} {client.Nick} {channel.Name} {characters}");
                }
                // TODO: creation time?

            } else if(args.SpacedArgs.Count > 2) {
                // Process
                string modes = args.SpacedArgs[2];

                if(args.SpacedArgs.Count == 3) {
                    args.SpacedArgs.Add("");
                }

                string currentModifier = "";
                string modeChars = "";
                string modeArgs = "";
                string modeString = "";
                bool announce = false;
                int position = 3; // start from spacedArgs[3]
                ChannelMode modeObject;
                var modesNoOperator = modes.Replace("+", "").Replace("-", "");
                if(ircd.GetSupportedModesByType()["A"].Contains(modesNoOperator)) {
                    // Is this mode of Type A (and listable)? See ModeType
                    // TODO: should we put this in the foreach?

                    // TODO: Some ircds make the ban list op only?
                    if(modesNoOperator == "b" && String.IsNullOrEmpty(args.SpacedArgs[3])) {
                        var banMode = (BanMode)channel.Modes["b"];
                        foreach(Ban ban in banMode.Bans.Values) {
                            client.Write($":{ircd.Host} {IrcNumeric.RPL_BANLIST.Printable()} {client.Nick} {channel.Name} {ban.GetBan()}");
                        }
                        client.Write($":{ircd.Host} {IrcNumeric.RPL_ENDOFBANLIST.Printable()} {client.Nick} {channel.Name} :End of channel ban list");
                        return true;
                    }
                }

                foreach(var mode in modes) {
                    var modeStr = mode.ToString();
                    var noOperatorMode = modeStr.Replace("+", "").Replace("-", "");
                    if(modeStr.Equals("+") || modeStr.Equals("-")) {
                        currentModifier = modeStr;
                        modeChars += modeStr;
                        continue;
                    }

                    if(channel.Modes.ContainsKey(noOperatorMode)) {
                        channel.Modes.TryGetValue(noOperatorMode, out modeObject);
                        if (!modeObject.Stackable) {
                            announce = true;
                        }
                        if(currentModifier == "+") {
                            // Attempt to add the mode
                            bool success = modeObject.Grant(client, args.SpacedArgs[position], args.Force, announce, announce);

                            if(success && modeObject.Stackable) {
                                modeChars += modeStr;
                                if(modeObject.HasParameters) {
                                    modeArgs += args.SpacedArgs[position] + " ";
                                }
                            }
                        } else if(currentModifier == "-") {
                            // Attempt to revoke the mode
                            bool success = modeObject.Revoke(client, args.SpacedArgs[position], args.Force, announce, announce);

                            if(success && modeObject.Stackable) {
                                modeChars += modeStr;
                                if(modeObject.HasParameters) {
                                    modeArgs += args.SpacedArgs[position] + " ";
                                }
                            }
                        }

                        if(modeObject.HasParameters) {
                            position += 1;
                        }
                    } else {
                        // If a mode doesn't exist, call it all off
                        throw new IrcErrNotEnoughParametersException(client, $"No such mode {noOperatorMode} exists");
                    }
                }

                if(!modeChars.Equals("+") && !modeChars.Equals("-")) {
                    modeString = $"{modeChars} {modeArgs}";
                    if(!modeString.Contains("+") && !modeString.Contains("-")) {
                        // Return if the mode string doesn't contain an operator
                        return true;
                    }
                    // Need to send UUID to room and not the nick if stacking
                    if(client.RemoteClient) {
                        if (modeString.Contains(" ")) {
                            var splitBySpaces = modeString.Split(' ');
                            foreach(var chunk in splitBySpaces) {
                                if(ircd.IsUUID(chunk)) {
                                    modeString = modeString.Replace(chunk, ircd.GetClientByUUID(chunk).Nick);
                                }
                            }
                        }
                    }
                    channel.SendToRoom(client, $":{client.Mask} MODE {channel.Name} {modeString}");

                    // TODO: Do we need to send this to all servers? Or just services?
                    // For now, send it to all
                    ircd.Servers.Where(server => server != client?.OriginServer).ForEach(
                        server => server.Write($":{client.Mask} MODE {channel.Name} {modeString}")
                    );
                }
            }

            return true;
        }


    }
}
