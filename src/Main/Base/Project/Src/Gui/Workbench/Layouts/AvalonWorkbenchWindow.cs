﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using AvalonDock;
using ICSharpCode.Core;
using ICSharpCode.Core.Presentation;

namespace ICSharpCode.SharpDevelop.Gui
{
	sealed class AvalonWorkbenchWindow : DocumentContent, IWorkbenchWindow, IOwnerState
	{
		readonly static string contextMenuPath = "/SharpDevelop/Workbench/OpenFileTab/ContextMenu";
		
		AvalonDockLayout dockLayout;
		
		public AvalonWorkbenchWindow(AvalonDockLayout dockLayout)
		{
			if (dockLayout == null)
				throw new ArgumentNullException("dockLayout");
			
			CustomFocusManager.SetRememberFocusedChild(this, true);
			this.IsFloatingAllowed = true;
			this.dockLayout = dockLayout;
			viewContents = new ViewContentCollection(this);
			
			ResourceService.LanguageChanged += OnTabPageTextChanged;
		}
		
		protected override void FocusContent()
		{
			if (!(IsActiveContent && !IsKeyboardFocusWithin))
				return;
			IInputElement activeChild = CustomFocusManager.GetFocusedChild(this);
			if (activeChild == null && ActiveViewContent != null) {
				activeChild = ActiveViewContent.InitiallyFocusedControl as IInputElement;
			}
			AvalonWorkbenchWindow.SetFocus(this, activeChild);
		}
		
		internal static void SetFocus(ManagedContent m, IInputElement activeChild, bool forceSetFocus = false)
		{
			if (activeChild != null) {
				LoggingService.Debug(m.Title + " - Will move focus to: " + activeChild);
				m.Dispatcher.BeginInvoke(
					DispatcherPriority.Background,
					new Action(
						delegate {
							// ensure that condition for FocusContent() is still fulfilled
							// (necessary to avoid focus switching loops when changing layouts)
							if (!forceSetFocus && !(m.IsActiveContent && !m.IsKeyboardFocusWithin)) {
								LoggingService.Debug(m.Title + " - not moving focus (IsActiveContent=" + m.IsActiveContent + ", IsKeyboardFocusWithin=" + m.IsKeyboardFocusWithin + ")");
								return;
							}
							LoggingService.Debug(m.Title + " - moving focus to: " + activeChild);
							Keyboard.Focus(activeChild);
						}));
			}
		}
		
		public bool IsDisposed { get { return false; } }
		
		#region IOwnerState
		[Flags]
		public enum OpenFileTabStates {
			Nothing             = 0,
			FileDirty           = 1,
			FileReadOnly        = 2,
			FileUntitled        = 4
		}
		
		public System.Enum InternalState {
			get {
				IViewContent content = this.ActiveViewContent;
				OpenFileTabStates state = OpenFileTabStates.Nothing;
				if (content != null) {
					if (content.IsDirty)
						state |= OpenFileTabStates.FileDirty;
					if (content.IsReadOnly)
						state |= OpenFileTabStates.FileReadOnly;
					if (content.PrimaryFile != null && content.PrimaryFile.IsUntitled)
						state |= OpenFileTabStates.FileUntitled;
				}
				return state;
			}
		}
		#endregion
		
		TabControl viewTabControl;
		
		/// <summary>
		/// The current view content which is shown inside this window.
		/// </summary>
		public IViewContent ActiveViewContent {
			get {
				WorkbenchSingleton.DebugAssertMainThread();
				if (viewTabControl != null && viewTabControl.SelectedIndex >= 0 && viewTabControl.SelectedIndex < ViewContents.Count) {
					return ViewContents[viewTabControl.SelectedIndex];
				} else if (ViewContents.Count == 1) {
					return ViewContents[0];
				} else {
					return null;
				}
			}
			set {
				int pos = ViewContents.IndexOf(value);
				if (pos < 0)
					throw new ArgumentException();
				SwitchView(pos);
			}
		}
		
		SDWindowsFormsHost GetActiveWinFormsHost()
		{
			if (viewTabControl != null && viewTabControl.SelectedIndex >= 0 && viewTabControl.SelectedIndex < ViewContents.Count) {
				TabItem page = (TabItem)viewTabControl.Items[viewTabControl.SelectedIndex];
				return page.Content as SDWindowsFormsHost;
			} else {
				return this.Content as SDWindowsFormsHost;
			}
		}
		
		public event EventHandler ActiveViewContentChanged;
		
		IViewContent oldActiveViewContent;
		
		void UpdateActiveViewContent()
		{
			UpdateTitle();
			IViewContent newActiveViewContent = this.ActiveViewContent;
			if (oldActiveViewContent != newActiveViewContent && ActiveViewContentChanged != null) {
				ActiveViewContentChanged(this, EventArgs.Empty);
			}
			oldActiveViewContent = newActiveViewContent;
			CommandManager.InvalidateRequerySuggested();
		}
		
		sealed class ViewContentCollection : Collection<IViewContent>
		{
			readonly AvalonWorkbenchWindow window;
			
			internal ViewContentCollection(AvalonWorkbenchWindow window)
			{
				this.window = window;
			}
			
			protected override void ClearItems()
			{
				foreach (IViewContent vc in this) {
					window.UnregisterContent(vc);
				}
				
				base.ClearItems();
				window.ClearContent();
				window.UpdateActiveViewContent();
			}
			
			protected override void InsertItem(int index, IViewContent item)
			{
				base.InsertItem(index, item);
				
				window.RegisterNewContent(item);
				
				if (Count == 1) {
					window.SetContent(item.Control, item);
				} else {
					if (Count == 2) {
						window.CreateViewTabControl();
						IViewContent oldItem = this[0];
						if (oldItem == item) oldItem = this[1];
						
						TabItem oldPage = new TabItem();
						oldPage.Header = StringParser.Parse(oldItem.TabPageText);
						oldPage.SetContent(oldItem.Control, oldItem);
						window.viewTabControl.Items.Add(oldPage);
					}
					
					TabItem newPage = new TabItem();
					newPage.Header = StringParser.Parse(item.TabPageText);
					newPage.SetContent(item.Control, item);
					
					window.viewTabControl.Items.Insert(index, newPage);
				}
				window.UpdateActiveViewContent();
			}
			
			protected override void RemoveItem(int index)
			{
				window.UnregisterContent(this[index]);
				
				base.RemoveItem(index);
				
				if (Count < 2) {
					window.ClearContent();
					if (Count == 1) {
						window.SetContent(this[0].Control, this[0]);
					}
				} else {
					window.viewTabControl.Items.RemoveAt(index);
				}
				window.UpdateActiveViewContent();
			}
			
			protected override void SetItem(int index, IViewContent item)
			{
				window.UnregisterContent(this[index]);
				
				base.SetItem(index, item);
				
				window.RegisterNewContent(item);
				
				if (Count == 1) {
					window.ClearContent();
					window.SetContent(item.Control, item);
				} else {
					TabItem page = (TabItem)window.viewTabControl.Items[index];
					page.SetContent(item.Control, item);
					page.Header = StringParser.Parse(item.TabPageText);
				}
				window.UpdateActiveViewContent();
			}
		}
		
		readonly ViewContentCollection viewContents;
		
		public IList<IViewContent> ViewContents {
			get { return viewContents; }
		}
		
		/// <summary>
		/// Gets whether any contained view content has changed
		/// since the last save/load operation.
		/// </summary>
		public bool IsDirty {
			get { return this.ViewContents.Any(vc => vc.IsDirty); }
		}
		
		public void SwitchView(int viewNumber)
		{
			if (viewTabControl != null) {
				this.viewTabControl.SelectedIndex = viewNumber;
			}
		}
		
		public void SelectWindow()
		{
			Activate();//this.SetAsActive();
		}
		
		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			
			if (this.DragEnabledArea != null) {
				this.DragEnabledArea.ContextMenu = MenuService.CreateContextMenu(this, contextMenuPath);
				UpdateTitle(); // set tooltip
			}
		}
		
		void Dispose()
		{
			ResourceService.LanguageChanged -= OnTabPageTextChanged;
			// DetachContent must be called before the controls are disposed
			List<IViewContent> viewContents = this.ViewContents.ToList();
			this.ViewContents.Clear();
			viewContents.ForEach(vc => vc.Dispose());
		}
		
		sealed class TabControlWithModifiedShortcuts : TabControl
		{
			readonly AvalonWorkbenchWindow parentWindow;
			
			public TabControlWithModifiedShortcuts(AvalonWorkbenchWindow parentWindow)
			{
				this.parentWindow = parentWindow;
			}
			
			protected override void OnKeyDown(KeyEventArgs e)
			{
				// We don't call base.KeyDown to prevent the TabControl from handling Ctrl+Tab.
				// Instead, we let the key press bubble up to the DocumentPane.
			}
			
			protected override void OnPreviewKeyDown(KeyEventArgs e)
			{
				base.OnPreviewKeyDown(e);
				if (e.Handled)
					return;
				
				// However, we do want to handle Ctrl+PgUp / Ctrl+PgDown (SD-1735)
				if ((e.Key == Key.PageUp || e.Key == Key.PageDown) && e.KeyboardDevice.Modifiers == ModifierKeys.Control) {
					int index = this.SelectedIndex;
					if (e.Key == Key.PageUp) {
						if (++index >= this.Items.Count)
							index = 0;
					} else {
						if (--index < 0)
							index = this.Items.Count - 1;
					}
					this.SelectedIndex = index;
					
					IViewContent vc = parentWindow.ActiveViewContent;
					if (vc != null)
						SetFocus(parentWindow, vc.InitiallyFocusedControl as IInputElement, true);
					
					e.Handled = true;
				}
			}
		}
		
		private void CreateViewTabControl()
		{
			if (viewTabControl == null) {
				viewTabControl = new TabControlWithModifiedShortcuts(this);
				viewTabControl.TabStripPlacement = Dock.Bottom;
				this.SetContent(viewTabControl);
				
				viewTabControl.SelectionChanged += delegate {
					UpdateActiveViewContent();
				};
			}
		}
		
		void ClearContent()
		{
			this.Content = null;
			if (viewTabControl != null) {
				foreach (TabItem page in viewTabControl.Items) {
					page.SetContent(null);
				}
				viewTabControl = null;
			}
		}
		
		void OnTitleNameChanged(object sender, EventArgs e)
		{
			if (sender == ActiveViewContent) {
				UpdateTitle();
			}
		}
		
		void OnIsDirtyChanged(object sender, EventArgs e)
		{
			UpdateTitle();
			CommandManager.InvalidateRequerySuggested();
		}
		
		void UpdateTitle()
		{
			IViewContent content = ActiveViewContent;
			if (content != null) {
				if (this.DragEnabledArea != null) {
					this.DragEnabledArea.ToolTip = content.PrimaryFileName;
				}
				
				string newTitle = content.TitleName;
				
				if (this.IsDirty) {
					newTitle += "*";
				}
				
				IsLocked = content.IsReadOnly;
				
				if (newTitle != Title) {
					Title = newTitle;
					OnTitleChanged(EventArgs.Empty);
				}
			}
		}
		
		void RegisterNewContent(IViewContent content)
		{
			Debug.Assert(content.WorkbenchWindow == null);
			content.WorkbenchWindow = this;
			
			content.TabPageTextChanged += OnTabPageTextChanged;
			content.TitleNameChanged   += OnTitleNameChanged;
			content.IsDirtyChanged     += OnIsDirtyChanged;
			
			this.dockLayout.Workbench.OnViewOpened(new ViewContentEventArgs(content));
		}
		
		void UnregisterContent(IViewContent content)
		{
			content.WorkbenchWindow = null;
			
			content.TabPageTextChanged -= OnTabPageTextChanged;
			content.TitleNameChanged   -= OnTitleNameChanged;
			content.IsDirtyChanged     -= OnIsDirtyChanged;
			
			this.dockLayout.Workbench.OnViewClosed(new ViewContentEventArgs(content));
		}
		
		void OnTabPageTextChanged(object sender, EventArgs e)
		{
			RefreshTabPageTexts();
		}
		
		bool forceClose;
		
		public bool CloseWindow(bool force)
		{
			WorkbenchSingleton.AssertMainThread();
			
			forceClose = force;
			Close();
			return this.ViewContents.Count == 0;
		}
		
		protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
		{
			base.OnClosing(e);
			if (!e.Cancel && !forceClose && this.IsDirty) {
				MessageBoxResult dr = MessageBox.Show(
					ResourceService.GetString("MainWindow.SaveChangesMessage"),
					ResourceService.GetString("MainWindow.SaveChangesMessageHeader") + " " + Title + " ?",
					MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
					MessageBoxResult.Yes);
				switch (dr) {
					case MessageBoxResult.Yes:
						foreach (IViewContent vc in this.ViewContents) {
							while (vc.IsDirty) {
								ICSharpCode.SharpDevelop.Commands.SaveFile.Save(vc);
								if (vc.IsDirty) {
									if (MessageService.AskQuestion("${res:MainWindow.DiscardChangesMessage}")) {
										break;
									}
								}
							}
						}
						break;
					case MessageBoxResult.No:
						break;
					case MessageBoxResult.Cancel:
						e.Cancel = true;
						break;
				}
			}
			if (!e.Cancel) {
				foreach (IViewContent vc in this.viewContents) {
					dockLayout.Workbench.StoreMemento(vc);
				}
			}
		}
		
		protected override void OnClosed()
		{
			base.OnClosed();
			Dispose();
			CommandManager.InvalidateRequerySuggested();
		}
		
		void RefreshTabPageTexts()
		{
			if (viewTabControl != null) {
				for (int i = 0; i < viewTabControl.Items.Count; ++i) {
					TabItem tabPage = (TabItem)viewTabControl.Items[i];
					tabPage.Header = StringParser.Parse(ViewContents[i].TabPageText);
				}
			}
		}
		
		void OnTitleChanged(EventArgs e)
		{
			if (TitleChanged != null) {
				TitleChanged(this, e);
			}
		}
		
		public event EventHandler TitleChanged;
		
		public override string ToString()
		{
			return "[AvalonWorkbenchWindow: " + this.Title + "]";
		}
		
		/// <summary>
		/// Gets the target for re-routing commands to this window.
		/// </summary>
		internal IInputElement GetCommandTarget()
		{
			return CustomFocusManager.GetFocusedChild(this) ?? GetActiveWinFormsHost();
		}
	}
}