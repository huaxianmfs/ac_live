﻿using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BilibiliDM_PluginFramework;
using Bililive_dm.Properties;

namespace Bililive_dm
{
    /// <summary>
    ///     App.xaml 的互動邏輯
    /// </summary>
    public partial class App : Application
    {
        public static readonly ObservableCollection<DMPlugin> Plugins = new ObservableCollection<DMPlugin>();

        public App()
        {
            AddArchSpecificDirectory();
            Application.Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
            try
            {
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            }
            catch (ConfigurationErrorsException ex)
            {
                //重置修复错误的配置文件
                var filename = ex.Filename;
                File.Delete(filename);
                Settings.Default.Reload();
            }

            var culture = CultureInfo.GetCultureInfo(Settings.Default.lang);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
        }

        internal Collection<ResourceDictionary> merged { get; private set; }

        public new static App Current => (App)Application.Current;

        private void AddArchSpecificDirectory()
        {
            var archPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                IntPtr.Size == 8 ? "x64" : "Win32");
            WINAPI.SetDllDirectory(archPath);
        }

        private void App_DispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("遇到了不明错误，日志已保存到 logs 文件夹");
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                System.IO.Directory.CreateDirectory(path);
                using (var outfile = new StreamWriter(System.IO.Path.Combine(path, "crash.txt")))
                {
                    outfile.WriteLine(DateTime.Now + "");
                    outfile.Write(e.Exception.ToString());
                    outfile.WriteLine("-------插件列表--------");
                    foreach (var dmPlugin in Plugins)
                        outfile.WriteLine(
                            $"{dmPlugin.PluginName}\t{dmPlugin.PluginVer}\t{dmPlugin.PluginAuth}\t{dmPlugin.PluginCont}\t启用:{dmPlugin.Status}");
                }
            }
            catch (Exception)
            {
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            merged = Resources.MergedDictionaries;
            merged.Add((ResourceDictionary)Resources["Default"]);
        }
    }
}