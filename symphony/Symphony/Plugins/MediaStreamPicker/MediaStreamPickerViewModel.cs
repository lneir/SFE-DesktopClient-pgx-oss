﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using HWND = System.IntPtr;

namespace Symphony.Plugins.MediaStreamPicker
{
    public class RequestShareEventArgs: EventArgs
    {
        public RequestShareEventArgs(string mediaStream, string fileName, string windowTitle)
        {
            this.mediaStream = mediaStream;
            this.fileName = fileName;
            this.windowTitle = windowTitle;
        }

        public string mediaStream { get; private set; }
        public string fileName { get; private set; }
        public string windowTitle { get; private set; }
    };

    public class Img
    {
        public Img(string value, BitmapSource img, string fileName)
        {
            Str = value;
            ImageSource = img;
            this.fileName = fileName;
        }
        public string Str { get; set; }
        public string fileName { get; private set; }

        public BitmapSource ImageSource { get; set; }
    }


    public class MediaStreamPickerViewModel : INotifyPropertyChanged
    {
        Window _viewWindow;
        private HWND _viewHwnd;

        List<string> mediaStreams = new List<string>();

        IList<EnumScreenResult> screens = null;
        IList<EnumWindowResult> windows = null;

        System.Threading.Timer _timer;
        object _locker = new object(); // lock for thread getting updates

        ObservableCollection<Img> streams = new ObservableCollection<Img>();
        int selectedIndex = -1;
        bool isShareEnabled = false;

        public MediaStreamPickerViewModel(Window window)
        {
            _viewWindow = window;

            this.CancelCommand = new CommandHandler(this.OnCancel);
            this.ShareCommand = new CommandHandler(this.OnShare);

            window.Loaded += window_Loaded;
            window.Unloaded += window_Unloaded;
        }

        void window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewHwnd = getWindowHwnd(_viewWindow);
            
            // initial build - this blocks ui - necessary to get window sized and centered correctly
            _onTimer(null);

            // runs on a seperate thread, because enumeration is expensive and interferes with ui thread.
            _timer = new System.Threading.Timer(_onTimer, null, new TimeSpan(0, 0, 3), new TimeSpan(0, 0, 3));
        }

        void window_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Dispose();
        }

        HWND getWindowHwnd(Window window)
        {
            Window win = Window.GetWindow(window);
            var wih = new WindowInteropHelper(win);
            return wih.Handle;
        }

        void _onTimer(object state)
        {
            if (System.Threading.Monitor.TryEnter(_locker))
            {
                try
                {
                    IList<EnumScreenResult> newScreens = EnumerateScreens.getScreens();
                    IList<EnumWindowResult> newWindows = EnumerateWindows.getWindows(_viewHwnd);

                    bool rebuild = false;

                    if (screens == null || windows == null ||
                        newWindows.Count != windows.Count || newScreens.Count != screens.Count)
                    {
                        rebuild = true;
                    }
                    else
                    {
                        foreach (EnumWindowResult window in windows)
                        {
                            bool found = false;
                            foreach (EnumWindowResult newWindow in newWindows)
                            {
                                if (newWindow.hWnd == window.hWnd && newWindow.title == window.title)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                rebuild = true;
                                break;
                            }
                        }
                    }

                    if (!rebuild)
                        return;

                    screens = newScreens;
                    windows = newWindows;

                    // signal to main thread to rebuild
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        this.rebuild();
                    }));
                }
                finally
                {
                    System.Threading.Monitor.Exit(_locker);
                }
            }
        }

        void rebuild()
        {
            streams.Clear();
            mediaStreams.Clear();

            // Note:
            // :0 needed on the end in chromium 49+
            // see discussion here: https://bitbucket.org/chromiumembedded/cef/issues/1065/add-support-for-webrtc-based-screen
            foreach (EnumScreenResult screen in screens)
            {
                addToStreams(screen.title, screen.image, "fullscreen");
                mediaStreams.Add("screen:" + screen.id + ":0");
            }
            foreach (EnumWindowResult window in windows)
            {
                addToStreams(window.title, window.image, window.filename.ToString());
                mediaStreams.Add("window:" + window.hWnd.ToString() + ":0");
            }

            Streams = streams;
            selectedIndex = -1;
        }

        void addToStreams(string title, BitmapSource image, string fileName)
        {
            Img item = new Img(title, image, fileName);
            streams.Add(item);
        }

        RequestShareEventArgs getSelectedMediaStream()
        {
            if (selectedIndex == -1 || selectedIndex < 0 || selectedIndex >= mediaStreams.Count)
                return null;
            RequestShareEventArgs args = new RequestShareEventArgs(mediaStreams[selectedIndex], streams[selectedIndex].fileName, streams[selectedIndex].Str);
            return args;
        }

        public ObservableCollection<Img> Streams
        {
            get { return this.streams; }
            set
            {
                this.streams = value;
                this.OnPropertyChanged("Streams");
            }
        }

        public int SelectedIndex
        {
            get { return this.selectedIndex;  }
            set
            {
                if (value == this.selectedIndex) return;
                this.selectedIndex = value;
                this.OnPropertyChanged("SelectedIndex");
                IsShareEnabled = this.selectedIndex != -1 ? true : false;
            }
        }

        public bool IsShareEnabled
        {
            get { return this.isShareEnabled; }
            set
            {
                if (value == this.isShareEnabled) return;
                this.isShareEnabled = value;
                this.OnPropertyChanged("IsShareEnabled");
            }
        }

        public event EventHandler<RequestShareEventArgs> RequestShare;
        public event EventHandler RequestCancel;

        public ICommand ShareCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        protected virtual void OnShare()
        {
            RequestShareEventArgs selectedMedia = getSelectedMediaStream();
            var onRequestShare = this.RequestShare;
            if (onRequestShare != null) onRequestShare(this, selectedMedia);
        }

        protected virtual void OnCancel()
        {
            var onCancel = this.RequestCancel;
            if (onCancel != null) onCancel(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private class CommandHandler : ICommand
        {
            private Action _action;
            public CommandHandler(Action action)
            {
                this._action = action;
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public event EventHandler CanExecuteChanged;

            public void Execute(object parameter)
            {
                this._action();
            }
        }
    }
}
