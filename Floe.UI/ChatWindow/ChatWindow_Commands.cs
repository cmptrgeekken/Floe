﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Floe.Net;

namespace Floe.UI
{
	public partial class ChatWindow : Window
	{
		public readonly static RoutedUICommand ChatCommand = new RoutedUICommand("Chat", "Chat", typeof(ChatWindow));
		public readonly static RoutedUICommand CloseCommand = new RoutedUICommand("Close", "Close", typeof(ChatWindow));
		public readonly static RoutedUICommand NewTabCommand = new RoutedUICommand("New Server Tab", "NewTab", typeof(ChatWindow));
		public readonly static RoutedUICommand DetachCommand = new RoutedUICommand("Detach", "Detach", typeof(ChatWindow));

		private void ExecuteChat(object sender, ExecutedRoutedEventArgs e)
		{
			var control = tabsChat.SelectedContent as ChatControl;
			this.BeginInvoke(() => App.Create(control.Session, new IrcTarget((string)e.Parameter), true));
		}

		private void ExecuteClose(object sender, ExecutedRoutedEventArgs e)
		{
			var context = e.Parameter as ChatContext;
			if (context != null)
			{
				if (context.Target == null)
				{
					if (context.Session.State == IrcSessionState.Disconnected || 
						this.Confirm(string.Format("Are you sure you want to disconnect from {0}?", context.Session.NetworkName),
						"Close Server Tab"))
					{
						if(context.Session.State != IrcSessionState.Disconnected)
						{
							context.Session.Quit("Leaving");
						}
						var itemsToRemove = (from i in this.Items
											 where i.Control.Session == context.Session
											 select i.Control.Context).ToArray();
						foreach(var item in itemsToRemove)
						{
							this.RemovePage(item);
						}
					}
				}
				else
				{
					if(context.Target.Type == IrcTargetType.Channel && context.Session.State != IrcSessionState.Disconnected)
					{
						context.Session.Part(context.Target.Name);
					}
					this.RemovePage(context);
				}
			}
		}

		private void ExecuteNewTab(object sender, ExecutedRoutedEventArgs e)
		{
			this.AddPage(new ChatContext(new IrcSession(), null), true);
		}

		private void ExecuteDetach(object sender, ExecutedRoutedEventArgs e)
		{
			var item = e.Parameter as ChatTabItem;
			if (item != null && !item.Control.IsServer)
			{
				this.Items.Remove(item);
				var ctrl = item.Control;
				item.Content = null;
				var window = new ChannelWindow(ctrl);
				window.Closed += new System.EventHandler(window_Closed);
				window.Show();
			}
		}

		private void window_Closed(object sender, System.EventArgs e)
		{
			var window = sender as ChannelWindow;
			if (window != null && window.Control.Parent == null)
			{
				for (int i = this.Items.Count - 1; i >= 0; --i)
				{
					if (this.Items[i].Control.Context.Session == window.Control.Session)
					{
						this.Items.Insert(i + 1, new ChatTabItem(window.Control));
						break;
					}
				}
			}
		}

		private void CanExecuteClose(object sender, CanExecuteRoutedEventArgs e)
		{
			var context = e.Parameter as ChatContext;
			if (context != null)
			{
				if (context.Target == null)
				{
					e.CanExecute = this.Items.Count((i) => i.Control.IsServer) > 1;
				}
				else
				{
					e.CanExecute = true;
				}
			}
		}
	}
}
