using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace cmpctircd.Packets {
    public static class Cap {

        [Handler("CAP", ListenerType.Client)]
        public static bool CapHandler(HandlerArgs args) {
            if (args.SpacedArgs.Count == 0) {
                throw new IrcErrNotEnoughParametersException(args.Client, "CAP");
            }

            var subCommand = args.SpacedArgs[1].ToUpper();

            switch (subCommand) {
                case "LS":
                    // Don't send welcome messages yet
                    args.Client.CapManager.StallRegistration(true);
                    CapHandleLS(args);
                    break;
                
                case "REQ":
                    // Don't send welcome messages yet
                    args.Client.CapManager.StallRegistration(true);
                    CapHandleReq(args);
                    break;

                case "END":
                    // Allow the welcome messages to be sent (and send them)
                    args.Client.CapManager.StallRegistration(false);
                    break;

                default:
                    // Unrecognised!
                    // At this point, try to recover by unstalling registration
                    args.Client.CapManager.StallRegistration(false);
                    throw new IrcErrInvalidCapCommandException(args.Client, subCommand);
            }

            return true;
        }

        public static void CapHandleLS(HandlerArgs args) {
            var version = 0;
            try {
                version = int.Parse(args.SpacedArgs.ElementAtOrDefault(2) ?? "0");
            } catch (FormatException) {
                throw new IrcErrNotEnoughParametersException(args.Client, "CAP");
            }

            var caps = string.Join(" ", args.Client.CapManager.GetAvailable(version).Select(cap => cap.Name));

            args.Client.Write($"CAP * LS :{caps}");
        }

        public static void CapHandleReq(HandlerArgs args) {
            var caps = new List<string>();
            var ackCaps = new List<string>(); // Caps we're going to ACK (successfully added)
            var badCaps = new List<string>(); // Caps we're going to NAK (could not be added)

            if (args.Trailer != null) {
                // Syntax: CAP REQ :<cap1> <cap2> ...
                caps = args.Trailer.Split(" ").ToList();
            } else {
                // Syntax: CAP REQ <cap1>
                // TODO: Ignore CAP REQ <cap1> <cap2> ...?
                caps.Add(args.SpacedArgs[2]);
            }

            var manager = args.Client.CapManager;
            foreach (var cap in caps) {
                try {
                    // Find cap with this name
                    var capObject = manager.Caps.First(c => c.Name == cap);
                    bool success;

                    if (cap.First() == '-') {
                        // This cap needs to be disabled
                        // e.g. CAP REQ :-away-notify
                        success = capObject.Disable();
                    } else {
                        // We're enabling the capability here
                        success = capObject.Enable();
                    }

                    if (success) {
                        ackCaps.Add(cap);
                    } else {
                        // A CAP can only be NAKed if the "requested capability change was rejected"
                        // Where the CAP was already enabled, we must pretend it was a successful change (keep enabled); do not NAK.
                        badCaps.Add(cap);
                    }
                } catch (InvalidOperationException) {
                    // Invalid capability requested
                    badCaps.Add(cap);
                }
            }

            // Send out the ACKs (successful)
            if (ackCaps.Any()) {
                args.Client.Write($"CAP * ACK: {string.Join(" ", ackCaps)}");
            }

            // Send out the NAKs (unsuccessful)
            if (badCaps.Any()) {
                args.Client.Write($"CAP * NAK: {string.Join(" ", badCaps)}");
            }
            
            return;
        }
    }
}