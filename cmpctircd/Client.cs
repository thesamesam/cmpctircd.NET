﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.Security;

using cmpctircd.Modes;

namespace cmpctircd
{
    public class Client {
        // Internals
        // TODO: Many of these look like they shouldn't be public or should be private set. Review?
        public IRCd IRCd { get; set; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream ClientStream { get; private set; }
        public SslStream ClientTlsStream { get; set; }
        public SocketListener Listener { get; set; }
        public byte[] Buffer { get; set; }

        // Metadata
        // TODO: Make these read-only apart from via setNick()?
        public String Nick { get; set; }
        public String Ident { get; set; }
        public String RealName { get; set; }
        public String AwayMessage { get; set; }
        public int IdleTime { get; set; }
        public int SignonTime = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        public ClientState State { get; set; }
        public List<Channel> Invites = new List<Channel>();

        // Ping information
        public Boolean WaitingForPong { get; set; } = false;
        public int LastPong { get; set; } = 0;
        public String PingCookie { get; set; } = "";

        public void SendVersion() => Write(String.Format(":{0} {1} {2} :cmpctircd-{3}", IRCd.Host, IrcNumeric.RPL_VERSION.Printable(), Nick, IRCd.Version));

        public readonly static object nickLock = new object();

        public Client(IRCd ircd, TcpClient tc, SocketListener sl) {
            Buffer = new byte[1024];

            State = ClientState.PreAuth;

            IRCd = ircd;
            TcpClient = tc;
            Listener = sl;

            IdleTime = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }

        ~Client() {
            ClientStream?.Close();
            TcpClient?.Close();
        }

        public void BeginTasks() {
            // Initialize the stream
            ClientStream = TcpClient.GetStream();
            // Check for PING/PONG events due (TODO: and DNS)
            CheckTimeout();
        }

        public void SendWelcome() {
            // Refuse if the user hasn't yet authenticated (or is already)
            if(String.IsNullOrWhiteSpace(Nick) || String.IsNullOrWhiteSpace(Ident)) return;
            if(State.CompareTo(ClientState.PreAuth) > 0) return;

            // Henceforth, we assume user can become Authenticated!
            State = ClientState.Auth;

            Write(String.Format(":{0} {1} {2} :Welcome to the {3} IRC Network {4}", IRCd.Host, IrcNumeric.RPL_WELCOME.Printable(), Nick, IRCd.Network, Mask));
            Write(String.Format(":{0} {1} {2} :Your host is {3}, running version cmpctircd-{4}", IRCd.Host, IrcNumeric.RPL_YOURHOST.Printable(), Nick, IRCd.Host, IRCd.Version));
            Write(String.Format(":{0} {1} {2} :This server was created {3}", IRCd.Host, IrcNumeric.RPL_CREATED.Printable(), Nick, IRCd.Host, 0));
            // TODO: This was commented out in the Perl version, probably not something to use for now.
            // TODO: I don't think MYINFO is very popular?
            //write(String.Format(":{0} {1} {2} {3} {4} x ntlo", ircd.Host, IrcNumeric.RPL_MYINFO.Printable(), nick, ircd.Host, ircd.version));
            var ModeTypes = IRCd.GetSupportedModesByType();
            var modes     = IRCd.GetSupportedModes(true);
            Write($":{IRCd.Host} {IrcNumeric.RPL_ISUPPORT.Printable()} {Nick} :CASEMAPPING=rfc1459 PREFIX=({modes["Characters"]}){modes["Symbols"]} STATUSMSG={modes["Symbols"]} NETWORK={IRCd.Network} MAXTARGETS={IRCd.MaxTargets} :are supported by this server");
            Write($":{IRCd.Host} {IrcNumeric.RPL_ISUPPORT.Printable()} {Nick} :CHANTYPES=# CHANMODES={string.Join("",ModeTypes["A"])},{string.Join("",ModeTypes["B"])},{string.Join("", ModeTypes["C"])},{string.Join("", ModeTypes["D"])} :are supported by this server");

            // Send MOTD
            SendMotd();
        }

        public void SendMotd() {
            try {
                string[] motd = System.IO.File.ReadAllLines("ircd.motd");
                Write(String.Format(":{0} {1} {2} : - {3} Message of the Day -", IRCd.Host, IrcNumeric.RPL_MOTDSTART.Printable(), Nick, IRCd.Host));
                for(int i = 0; i < motd.Length; i++) {
                    if((i == motd.Length) && String.IsNullOrEmpty(motd[i])) {
                        // If end of the file and a new line, don't print.
                        break;
                    }
                    Write(String.Format(":{0} {1} {2} : - {3}", IRCd.Host, IrcNumeric.RPL_MOTD.Printable(), Nick, motd[i]));
                }
                Write(String.Format(":{0} {1} {2} :End of /MOTD command.", IRCd.Host, IrcNumeric.RPL_ENDOFMOTD.Printable(), Nick));
            } catch(System.IO.FileNotFoundException) {
                Console.WriteLine("ircd.motd doesn't exist!");
            }
        }


        public void SendRules() {
            try {
                string[] rules = System.IO.File.ReadAllLines("ircd.rules");
                Write(String.Format(":{0} {1} {2} :- {3} server rules -", IRCd.Host, IrcNumeric.RPL_MOTDSTART.Printable(), Nick, IRCd.Host));
                for(int i = 0; i < rules.Length; i++) {
                    if((i == rules.Length) && String.IsNullOrEmpty(rules[i])) {
                        // If end of the file and a new line, don't print.
                        break;
                    }
                    Write(String.Format(":{0} {1} {2} : - {3}", IRCd.Host, IrcNumeric.RPL_MOTD.Printable(), Nick, rules[i]));
                }
                Write(String.Format(":{0} {1} {2} :End of RULES command.", IRCd.Host, IrcNumeric.RPL_ENDOFMOTD.Printable(), Nick));
            } catch(System.IO.FileNotFoundException) {
                Console.WriteLine("ircd.rules doesn't exist!");
            }
        }

        public Boolean SetNick(String nick) {
            lock(nickLock) {
                // Return if nick is the same
                String oldNick = this.Nick;
                String newNick = nick;

                if (String.Compare(newNick, oldNick, false) == 0)
                    return true;

                // Is the nick safe?
                Regex safeNicks = new Regex(@"[A-Za-z{}\[\]_\\^|`][A-Za-z{}\[\]_\-\\^|`0-9]*", RegexOptions.IgnoreCase);
                Boolean safeNick = safeNicks.Match(newNick).Success;
                if (!safeNick) {
                    throw new IrcErrErroneusNicknameException(this, newNick);
                }


                // Does a user with this nick already exist?
                try {
                    IRCd.GetClientByNick(newNick);
                    throw new IrcErrNicknameInUseException(this, newNick);
                } catch(InvalidOperationException) {}

                foreach(var channel in IRCd.ChannelManager.Channels) {
                    if(!channel.Value.Inhabits(this)) continue;
                    channel.Value.SendToRoom(this, String.Format(":{0} NICK :{1}", Mask, newNick), false);
                    channel.Value.Remove(oldNick, false, false);
                    channel.Value.Add(this, newNick);
                }

                // TODO: Verify the nickname is safe
                Write(String.Format(":{0} NICK {1}", Mask, nick));
                this.Nick = newNick;

                SendWelcome();
                return true;
            }
        }

        public Boolean SetUser(String ident, String real_name) {
            // TOOD: validation
            this.Ident = ident;
            this.RealName = real_name;

            SendWelcome();
            // this.sendWelcome() a lá cmpctircd?
            return true;
        }

        public void CheckTimeout() {
            // By default, no pong cookie is required
            Int32 time = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            Boolean requirePong = false;
            int period = (LastPong) + (IRCd.PingTimeout);

            requirePong = (IRCd.RequirePong) && (LastPong == 0);

            // Is the user due a PING?
            if((requirePong) || (time > period && !WaitingForPong)) {
                PingCookie = CreatePingCookie();
                LastPong = time;
                WaitingForPong = true;

                Write(String.Format("PING :{0}", PingCookie));
            }

            // Has the user taken too long to reply with a PONG?
            if(WaitingForPong && (time > (LastPong + (IRCd.PingTimeout * 2)))) {
                Disconnect("Ping timeout", true);
            }

            Task.Delay(60000).ContinueWith(t => CheckTimeout());
        }

        public static String CreatePingCookie() => System.IO.Path.GetRandomFileName().Substring(0, 7);

        // Returns the user's host (raw IP)
        // TODO: DNS?
        public String Host => ((System.Net.IPEndPoint)TcpClient.Client.RemoteEndPoint).Address.ToString();

        // Returns the user's mask
        // TODO: cloaking
        public String Mask
        {
            get
            {
                String nick = this.Nick;
                String user = this.Ident;
                String real_name = this.RealName;
                String host = this.Host;
                return String.Format("{0}!{1}@{2}", nick, user, host);
            }
        }

        public void Write(String packet) {
            byte[] packetBytes = Encoding.UTF8.GetBytes(packet + "\r\n");
            try {
                if(ClientTlsStream != null) {
                    ClientTlsStream.Write(packetBytes, 0, packetBytes.Length);
                } else {
                    ClientStream.Write(packetBytes, 0, packetBytes.Length);
                }
            } catch(System.IO.IOException) {
                Disconnect("Connection reset by host", true, false);
            }
        }

        public void Disconnect(Boolean graceful) => Disconnect("", graceful, true);
        public void Disconnect(String quitReason, Boolean graceful) => Disconnect(quitReason, graceful, true);

        public void Disconnect(String quitReason, Boolean graceful, Boolean sendToSelf) {
            if (graceful) {
                // Inform all of the channels we're a member of that we are leaving
                foreach (var channel in IRCd.ChannelManager.Channels) {
                    if (channel.Value.Inhabits(this)) {
                        channel.Value.Quit(this, quitReason);
                        channel.Value.Remove(this, true);
                    }
                }
            }

            if (sendToSelf) {
                // Need this flag to prevent infinite loop of calls to Disconnect() upon IOException
                // No need to guard the Channel quit because they do not send to the user leaving
                Write($":{Mask} QUIT :{quitReason}");
            }

            Listener.Remove(this);
        }

    }
}
