using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CitySimulation;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SimulationCrossplatform
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var args = desktop.Args.ToList();
                if (args.Count < 1 && File.Exists("UPDESUA.json"))
                {
                    args.Add("UPDESUA.json");
                }

                if (args.Count < 1 || !File.Exists(args[0]))
                {
                    MessageBox.Show("Invalid arguments. Provide path to configuration file.", "Error", MessageBox.MessageBoxButtons.Ok);
                    return;
                }

                try
                {
                    desktop.MainWindow = new MainWindow();
                    ((MainWindow)desktop.MainWindow).Setup(args[0]);
                }
                catch (JsonSerializationException e)
                {
                    MessageBox.Show("Invalid configuration file", "Error", MessageBox.MessageBoxButtons.Ok);
                    return;
                }

            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
