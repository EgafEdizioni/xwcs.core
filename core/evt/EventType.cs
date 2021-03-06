﻿namespace xwcs.core.evt
{
    public class EventType
    {
        public static readonly object GenericEvent = new object();
        public static readonly object CloseEvent = new object();
        public static readonly object OpenPanelRequestEvent = new object();
        public static readonly object AddToolBarRequestEvent = new object();
        public static readonly object OutputMessageEvent = new object();
        public static readonly object DocumentChangedEvent = new object();
        public static readonly object VisualControlActionEvent = new object();
        public static readonly object WorkSpaceLoadedEvent = new object();
		public static readonly object SetTitleEvent = new object();
		public static readonly object ClosePanelRequestEvent = new object();
	}
}
