﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WindowsInput;
using WindowsInput.Native;
using NHotkey;
using NHotkey.Wpf;
using Wox.Commands;
using Wox.Infrastructure;
using Wox.Infrastructure.UserSettings;
using Wox.Plugin;
using Wox.PluginLoader;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Forms.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Forms.MenuItem;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Timers.Timer;

namespace Wox
{
    public partial class MainWindow
    {
        private static readonly object locker = new object();
        private readonly GloablHotkey globalHotkey = new GloablHotkey();

        private readonly KeyboardSimulator keyboardSimulator = new KeyboardSimulator(new InputSimulator());
        private readonly Storyboard progressBarStoryboard = new Storyboard();
        private bool WinRStroked;
        private NotifyIcon notifyIcon;
        private bool queryHasReturn;

        public MainWindow()
        {
            InitializeComponent();

            InitialTray();
            resultCtrl.resultItemChangedEvent += resultCtrl_resultItemChangedEvent;
            ThreadPool.SetMaxThreads(30, 10);
            InitProgressbarAnimation();
            try
            {
                SetTheme(CommonStorage.Instance.UserSetting.Theme);
            }
            catch (IOException)
            {
                SetTheme(CommonStorage.Instance.UserSetting.Theme = "Default");
            }


            Closing += MainWindow_Closing;
        }

        public void SetHotkey(string hotkeyStr,EventHandler<HotkeyEventArgs> action)
        {
            HotkeyModel hotkey = new HotkeyModel(hotkeyStr);
            try
            {
                HotkeyManager.Current.AddOrReplace(hotkeyStr, hotkey.CharKey, hotkey.ModifierKeys,action);
            }
            catch (Exception)
            {
                MessageBox.Show("Registe hotkey: " + CommonStorage.Instance.UserSetting.Hotkey + " failed.");
            }
        }

        public void RemoveHotkey(string hotkeyStr)
        {
            HotkeyManager.Current.Remove(hotkeyStr);
        }

        private void SetCustomPluginHotkey()
        {
            foreach (CustomPluginHotkey hotkey in CommonStorage.Instance.UserSetting.CustomPluginHotkeys)
            {
                CustomPluginHotkey hotkey1 = hotkey;
                SetHotkey(hotkey.Hotkey,delegate
                {
                    ShowApp();
                    ChangeQuery(hotkey1.ActionKeyword);
                });
            }
        }

        private void OnHotkey(object sender, HotkeyEventArgs e)
        {
            if (!IsVisible)
            {
                ShowWox();
            }
            else
            {
                HideWox();
            }
            e.Handled = true;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void WakeupApp()
        {
            //After hide wox in the background for a long time. It will become very slow in the next show.
            //This is caused by the Virtual Mermory Page Mechanisam. So, our solution is execute some codes in every min
            //which may prevent sysetem uninstall memory from RAM to disk.

            var t = new Timer(1000 * 60 * 5) { AutoReset = true, Enabled = true };
            t.Elapsed += (o, e) => Dispatcher.Invoke(new Action(() =>
            {
                if (Visibility != Visibility.Visible)
                {
                    double oldLeft = Left;
                    Left = 20000;
                    ShowWox();
                    CommandFactory.DispatchCommand(new Query("qq"), false);
                    HideWox();
                    Left = oldLeft;
                }
            }));
        }

        private void InitProgressbarAnimation()
        {
            var da = new DoubleAnimation(progressBar.X2, Width + 100, new Duration(new TimeSpan(0, 0, 0, 0, 1600)));
            var da1 = new DoubleAnimation(progressBar.X1, Width, new Duration(new TimeSpan(0, 0, 0, 0, 1600)));
            Storyboard.SetTargetProperty(da, new PropertyPath("(Line.X2)"));
            Storyboard.SetTargetProperty(da1, new PropertyPath("(Line.X1)"));
            progressBarStoryboard.Children.Add(da);
            progressBarStoryboard.Children.Add(da1);
            progressBarStoryboard.RepeatBehavior = RepeatBehavior.Forever;
            progressBar.Visibility = Visibility.Hidden;
            progressBar.BeginStoryboard(progressBarStoryboard);
        }

        private void InitialTray()
        {
            notifyIcon = new NotifyIcon { Text = "Wox", Icon = Properties.Resources.app, Visible = true };
            notifyIcon.Click += (o, e) => ShowWox();
            var open = new MenuItem("Open");
            open.Click += (o, e) => ShowWox();
            var exit = new MenuItem("Exit");
            exit.Click += (o, e) => CloseApp();
            MenuItem[] childen = { open, exit };
            notifyIcon.ContextMenu = new ContextMenu(childen);
        }

        private void resultCtrl_resultItemChangedEvent()
        {
            resultCtrl.Margin = resultCtrl.GetCurrentResultCount() > 0
                ? new Thickness { Top = grid.Margin.Top }
                : new Thickness { Top = 0 };
        }

        private void TextBoxBase_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            resultCtrl.Dirty = true;
            Dispatcher.DelayInvoke("UpdateSearch",
                o =>
                {
                    Dispatcher.DelayInvoke("ClearResults", i =>
                    {
                        // first try to use clear method inside resultCtrl, which is more closer to the add new results
                        // and this will not bring splash issues.After waiting 30ms, if there still no results added, we
                        // must clear the result. otherwise, it will be confused why the query changed, but the results
                        // didn't.
                        if (resultCtrl.Dirty) resultCtrl.Clear();
                    }, TimeSpan.FromMilliseconds(30), null);
                    var q = new Query(tbQuery.Text);
                    CommandFactory.DispatchCommand(q);
                    queryHasReturn = false;
                    if (Plugins.HitThirdpartyKeyword(q))
                    {
                        Dispatcher.DelayInvoke("ShowProgressbar", originQuery =>
                        {
                            if (!queryHasReturn && originQuery == tbQuery.Text)
                            {
                                StartProgress();
                            }
                        }, TimeSpan.FromSeconds(1), tbQuery.Text);
                    }
                }, TimeSpan.FromMilliseconds(150));
        }

        private void StartProgress()
        {
            progressBar.Visibility = Visibility.Visible;
        }

        private void StopProgress()
        {
            progressBar.Visibility = Visibility.Hidden;
        }

        private void HideWox()
        {
            Hide();
        }

        private void ShowWox(bool selectAll = true)
        {
            Show();
            Activate();
            Focus();
            tbQuery.Focus();
            if (selectAll) tbQuery.SelectAll();
        }

        public void ParseArgs(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "reloadplugin":
                        Plugins.Init();
                        break;

                    case "query":
                        if (args.Length > 1)
                        {
                            string query = args[1];
                            tbQuery.Text = query;
                            tbQuery.SelectAll();
                        }
                        break;

                    case "hidestart":
                        HideApp();
                        break;
                }
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 3;

            SetHotkey(CommonStorage.Instance.UserSetting.Hotkey,OnHotkey);
            SetCustomPluginHotkey();
            WakeupApp();
            Plugins.Init();

            globalHotkey.hookedKeyboardCallback += KListener_hookedKeyboardCallback;
        }

        private bool KListener_hookedKeyboardCallback(KeyEvent keyevent, int vkcode, SpecialKeyState state)
        {
            if (CommonStorage.Instance.UserSetting.ReplaceWinR)
            {
                //todo:need refatoring. move those codes to CMD file or expose events
                if (keyevent == KeyEvent.WM_KEYDOWN && vkcode == (int)Keys.R && state.WinPressed)
                {
                    WinRStroked = true;
                    Dispatcher.BeginInvoke(new Action(OnWinRPressed));
                    return false;
                }
                if (keyevent == KeyEvent.WM_KEYUP && WinRStroked && vkcode == (int)Keys.LWin)
                {
                    WinRStroked = false;
                    keyboardSimulator.ModifiedKeyStroke(VirtualKeyCode.LWIN, VirtualKeyCode.CONTROL);
                    return false;
                }
            }
            return true;
        }

        private void OnWinRPressed()
        {
            ShowWox(false);
            if (tbQuery.Text != ">")
            {
                resultCtrl.Clear();
                ChangeQuery(">");
            }
            tbQuery.CaretIndex = tbQuery.Text.Length;
        }

        private void TbQuery_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            //when alt is pressed, the real key should be e.SystemKey
            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
            switch (key)
            {
                case Key.Escape:
                    HideWox();
                    e.Handled = true;
                    break;

                case Key.Down:
                    resultCtrl.SelectNext();
                    e.Handled = true;
                    break;

                case Key.Up:
                    resultCtrl.SelectPrev();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    AcceptSelect();
                    e.Handled = true;
                    break;
            }
        }

        private void AcceptSelect()
        {
            Result result = resultCtrl.AcceptSelect();
            if (result != null)
            {
                CommonStorage.Instance.UserSelectedRecords.Add(result);
                if (!result.DontHideWoxAfterSelect)
                {
                    HideWox();
                }
            }
        }

        public void OnUpdateResultView(List<Result> list)
        {
            queryHasReturn = true;
            progressBar.Dispatcher.Invoke(new Action(StopProgress));
            if (list.Count > 0)
            {
                //todo:this should be opened to users, it's their choise to use it or not in thier workflows
                list.ForEach(
                    o =>
                    {
                        if (o.AutoAjustScore) o.Score += CommonStorage.Instance.UserSelectedRecords.GetSelectedCount(o);
                    });
                lock (locker)
                {
                    resultCtrl.Dispatcher.Invoke(new Action(() =>
                    {
                        List<Result> l =
                            list.Where(o => o.OriginQuery != null && o.OriginQuery.RawQuery == tbQuery.Text).ToList();
                        resultCtrl.AddResults(list);
                    }));
                }
            }
        }

        public void SetTheme(string themeName)
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/" + themeName + ".xaml")
            };

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        #region Public API

        public void ChangeQuery(string query)
        {
            tbQuery.Text = query;
            tbQuery.CaretIndex = tbQuery.Text.Length;
        }

        public void CloseApp()
        {
            notifyIcon.Visible = false;
            Environment.Exit(0);
        }

        public void HideApp()
        {
            HideWox();
        }

        public void ShowApp()
        {
            ShowWox();
        }

        public void ShowMsg(string title, string subTitle, string iconPath)
        {
            var m = new Msg { Owner = GetWindow(this) };
            m.Show(title, subTitle, iconPath);
        }

        public void OpenSettingDialog()
        {
            new SettingWidow(this).Show();
        }

        #endregion
    }
}