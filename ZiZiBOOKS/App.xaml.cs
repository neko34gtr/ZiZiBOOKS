using System;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace ZiZiBOOKS
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // [ADD] 二重起動防止
        // 同じ名前のMutexを他プロセスと取り合うことで、2つ目以降の起動を検知する
        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _showRequestEvent;

        private const string MutexName = "ZiZiBOOKS_SingleInstance_9F2E6C1A";
        private const string ShowEventName = "ZiZiBOOKS_ShowRequest_9F2E6C1A";

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // [ADD] 既に起動中のインスタンスへ「表示」シグナルを送ってから、
                // 自分自身（2つ目のプロセス）は何も生成せず即座に終了する
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                    existing.Set();
                }
                catch { /* 既存インスタンスが応答できない場合は無視して終了のみ行う */ }

                Environment.Exit(0);
                return;
            }

            // [ADD] 自分が最初のインスタンス：他プロセスからの表示リクエストを監視するスレッドを開始
            _showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            StartShowRequestListener();

            base.OnStartup(e);
        }

        private void StartShowRequestListener()
        {
            var listener = new Thread(() =>
            {
                while (true)
                {
                    _showRequestEvent!.WaitOne();
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is MainWindow mw)
                        {
                            mw.RequestShow();
                        }
                    });
                }
            })
            {
                IsBackground = true
            };
            listener.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
