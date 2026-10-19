using System.Drawing;

namespace PingTray
{
    /// <summary>
    /// Whether the mouse pointer is over the tray icon. NotifyIcon has no mouse-leave event, but
    /// Windows sends it mouse moves while the pointer moves over the icon. So the pointer is still
    /// there if it has not moved since the last mouse move, or one came in the last LeaveMs; once
    /// it has moved without one for longer, it has left.
    /// </summary>
    public class HoverTracker
    {
        public const int LeaveMs = 400;

        Point lastPosition;
        int lastMoveTick;
        bool hovering;

        public bool Hovering
        {
            get { return hovering; }
        }

        /// <summary>A mouse move over the icon. Returns true when it starts a hover.</summary>
        public bool Moved(Point position, int tick)
        {
            lastPosition = position;
            lastMoveTick = tick;
            if (hovering)
                return false;
            hovering = true;
            return true;
        }

        /// <summary>Called a few times a second while hovering. Returns true when the hover has just ended.</summary>
        public bool Poll(Point position, int tick)
        {
            if (!hovering || position == lastPosition || unchecked(tick - lastMoveTick) < LeaveMs)
                return false;
            hovering = false;
            return true;
        }
    }
}
