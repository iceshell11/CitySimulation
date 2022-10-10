using Avalonia.Controls;
using CitySimulation;
using CitySimulation.Control;
using CitySimulation.Ver2.Control;
using CitySimulation.Ver2.Generation;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CitySimulation.Entities;
using CitySimulation.Ver2.Entity;
using CitySimulation.Ver2.Generation.Osm;
using SimulationCrossplatform.Render;
using Newtonsoft.Json;
using SimulationCrossplatform.Controls;
using CitySimulation.Health;
using ScottPlot;

namespace SimulationCrossplatform
{
    public partial class MainWindow : Window
    {
        private Controller controller;
        private int _numThreads;
        private DateTime _lastTime = DateTime.Now;
        private Dictionary<string, string> _facilityColors;
        private Dictionary<RealtimePlot, Func<(int, int)>> _plots = new ();
        private string _configPath;
        public MainWindow()
        {
            InitializeComponent();
        }

        public void Setup(string configPath)
        {
            _configPath = configPath;
            controller = GenerateOsmController(configPath);
            var drawConfig = JsonConvert.DeserializeObject<DrawJsonConfig>(File.ReadAllText(configPath));
            SimulationCanvas.Setup(new TileRenderer()
            {
                ZoomClose = drawConfig.ZoomClose,
                ZoomFar = drawConfig.ZoomFar,
                TilesDirectory = drawConfig.TilesDirectory,
                VisibleArea = drawConfig.TilesRenderDistance
            });

            SimulationCanvas.SetFacilityRenderers(controller.City.Facilities, _facilityColors);

            DeltaTime.Value = controller.DeltaTime;

            controller.OnLifecycleFinished += Controller_OnLifecycleFinished;
            controller.OnFinished += ResetModel;

            SetupVisibilityLayers(controller.City.Facilities);
            SetupPlots(drawConfig);
            double aX = controller.City.Facilities.Values.OfType<FacilityConfigurable>().Select(x => x.Coords.X).Average();
            double aY = controller.City.Facilities.Values.OfType<FacilityConfigurable>().Select(x => x.Coords.Y).Average();
            SimulationCanvas.DrawPoint = new Avalonia.Point(-aX, -aY).MapToScreen();

            SimulationCanvas.Update(controller);

            Task.Run(() =>
            {
                controller.Setup(ProgressCallback);
                Dispatcher.UIThread.InvokeAsync(OnModelReady);
            });
        }

        private void ProgressCallback(int value)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressBar.IsVisible = value < 100;
                ProgressBar.Value = value;
            });
        }

        private void OnModelReady()
        {
            ProgressCallback(100);
            SimulationCanvas.InvalidateVisual();
            StartButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
        }

        private void ResetModel()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StartButton.IsEnabled = false;
                PauseButton.IsEnabled = false;
                StopButton.IsEnabled = false;
            });

            var (city, config, random) = GenerateOsmCity(_configPath);
            controller.City = city;
            controller.Context = new Context()
            {
                Random = random,
                CurrentTime = new CityTime(),
                Params = config.Params,
            };

            Dispatcher.UIThread.InvokeAsync(SimulationCanvas.InvalidateVisual);

            Task.Run(() =>
            {
                controller.Setup();
                Dispatcher.UIThread.InvokeAsync(OnModelReady);
            });
        }

        void SetupVisibilityLayers(FacilityManager facilities)
        {
            AddVisibilityLayer("tiles");
            AddVisibilityLayer("route");
            AddVisibilityLayer("people");
            AddVisibilityLayer("[people in transport]");
            AddVisibilityLayer("[facility names]", false);

            foreach (var facilityType in facilities.Values.Select(x => x.Type).Distinct())
            {
                Color? color = null;

                if (_facilityColors.ContainsKey(facilityType))
                {
                    string colorText = _facilityColors[facilityType];

                    if (!Color.TryParse(colorText, out var parsedColor))
                    {
                        parsedColor = uint.TryParse(colorText, out uint colorCode) ? Color.FromUInt32(colorCode) : Colors.Black;
                    }

                    color = parsedColor;
                }

                AddVisibilityLayer(facilityType, true, color);
            }
        }
        void SetupPlots(DrawJsonConfig drawConfig)
        {
            Plot1.Plot.Title("Infected count");
            Plot1.Step = (int)(drawConfig.PlotStep * 24 * 60);
            Plot1.RenderStep = (int)(drawConfig.PlotRedrawStep * 24 * 60);
            Plot1.Scale = drawConfig.PlotScale;

            SetPlotFunc(Plot1, () => (controller.Context.CurrentTime.TotalMinutes, controller.City.Persons.Count(x => x.HealthData.Infected)));

            Plot2.Plot.Title("Average contacts count per day");
            Plot2.Step = (int)(drawConfig.PlotStep * 24 * 60);
            Plot2.RenderStep = (int)(drawConfig.PlotRedrawStep * 24 * 60);
            Plot2.Scale = drawConfig.PlotScale;

            // SetPlotFunc(Plot2, () => (controller.Context.CurrentTime.TotalMinutes, controller.City.Persons.Count(x => x.HealthData.HealthStatus == HealthStatus.Recovered)));
            PeriodicWriteModule periodicWriteModule = controller.Modules.OfType<PeriodicWriteModule>().FirstOrDefault();
            if (periodicWriteModule != null)
            {
                periodicWriteModule.OnLogFlush += data =>
                {
                    Plot2.AddPoint((controller.Context.CurrentTime.TotalMinutes, (float)data["Average contacts count per day"]));
                };
            }
        }

        private void AddVisibilityLayer(string layer, bool defaultValue = true, Color? color = null)
        {
            CheckBox checkBox = new CheckBox()
            {
                Content = new TextBlock(){ Text = layer },
                IsChecked = defaultValue
            };

            if (color.HasValue)
            {
                checkBox.Content = new StackPanel()
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock() { Text = layer },
                        new Rectangle() { Fill = new SolidColorBrush(color.Value), Width = 15, Height = 15, Margin = new Thickness(5, 0)},
                    }
                };
            }

            checkBox.Unchecked += VisibilityCheckBoxOnValueChanged;
            checkBox.Checked += VisibilityCheckBoxOnValueChanged;

            VisibilityPanel.Children.Add(checkBox);
            SimulationCanvas.SetVisibility(layer, defaultValue);
        }

        private void VisibilityCheckBoxOnValueChanged(object sender, RoutedEventArgs e)
        {
            CheckBox c = (CheckBox)sender;

            if (c.Content is TextBlock textBlock)
            {
                SimulationCanvas.SetVisibility(textBlock.Text, c.IsChecked == true);
            }
            else if (c.Content is StackPanel panel)
            {
                TextBlock block = panel.Children.OfType<TextBlock>().First();
                SimulationCanvas.SetVisibility(block.Text, c.IsChecked == true);
            }

            SimulationCanvas.InvalidateVisual();
        }

        private void SetPlotFunc(RealtimePlot plot, Func<(int, int)> func)
        {
            _plots.Remove(plot);
            _plots.Add(plot, func);
        }

        private Controller GenerateSimple()
        {
            ModelSimple model = new ModelSimple()
            {
                FileName = "UPDESUA.json",
                UseTransport = true
            };

            RunConfig config = model.Configuration();

            Random random = new Random(config.Seed);

            City city = model.Generate(random);


            var controller = new ControllerSimple()
            {
                City = city,
                Context = new Context()
                {
                    Random = random,
                    CurrentTime = new CityTime(),
                    Params = config.Params,
                },
                DeltaTime = config.DeltaTime
            };

            Directory.CreateDirectory("output");


            PeriodicWriteModule traceModule = null;

            if (config.TraceDeltaTime.HasValue && config.TraceDeltaTime > 0)
            {
                TraceChangesModule traceChangesModule = new TraceChangesModule()
                {
                    Filename = "output/changes.txt",
                    LogDeltaTime = config.TraceDeltaTime.Value,
                    PrintConsole = config.TraceConsole,
                };

                controller.Modules.Add(traceChangesModule);
            }

            if (config.LogDeltaTime.HasValue && config.LogDeltaTime > 0)
            {
                traceModule = new PeriodicWriteModule()
                {
                    Filename = "output/table.csv",
                    LogDeltaTime = config.LogDeltaTime.Value,
                    PrintConsole = config.PrintConsole,
                };
                controller.Modules.Add(traceModule);
            }
            
            controller.Setup();

            controller.OnLifecycleFinished += () =>
            {
                if (controller.Context.CurrentTime.Day >= config.DurationDays)
                {
                    Console.WriteLine("---------------------");
                    Controller.IsRunning = false;
                }
            };

            _numThreads = config.NumThreads;

            //Запуск симуляции
            // controller.RunAsync(config.NumThreads);

            return controller;
        }

        private Controller GenerateOsmController(string configPath)
        {
            var (city, config, random) = GenerateOsmCity(configPath);


            var controller = new ControllerSimple()
            {
                City = city,
                Context = new Context()
                {
                    Random = random,
                    CurrentTime = new CityTime(),
                    Params = config.Params,
                },
                DeltaTime = config.DeltaTime
            };

            Directory.CreateDirectory("output");


            PeriodicWriteModule traceModule = null;

            if (config.TraceDeltaTime.HasValue && config.TraceDeltaTime > 0)
            {
                TraceChangesModule traceChangesModule = new TraceChangesModule()
                {
                    Filename = "output/changes.txt",
                    LogDeltaTime = config.TraceDeltaTime.Value,
                    PrintConsole = config.TraceConsole,
                };

                controller.Modules.Add(traceChangesModule);
            }

            if (config.LogDeltaTime.HasValue && config.LogDeltaTime > 0)
            {
                traceModule = new PeriodicWriteModule()
                {
                    Filename = "output/table.csv",
                    LogDeltaTime = config.LogDeltaTime.Value,
                    PrintConsole = config.PrintConsole,
                };
                controller.Modules.Add(traceModule);
            }

            controller.OnLifecycleFinished += () =>
            {
                if (controller.Context.CurrentTime.Day >= config.DurationDays)
                {
                    Console.WriteLine("---------------------");
                    Controller.IsRunning = false;
                }
            };

            _numThreads = config.NumThreads;
            
            return controller;
        }

        private (City city, RunConfig config, Random random) GenerateOsmCity(string configPath)
        {
            var model = new OsmModel()
            {
                FileName = configPath,
                UseTransport = true
            };


            _facilityColors = model.FacilityColors();

            RunConfig config = model.Configuration();

            var random = new Random(config.Seed);

            City city = model.Generate(random);

            return (city, config, random);
        }

        private void StartSimulation()
        {
            Task.Run(() =>
            {
                controller.RunAsync(_numThreads);
            });
        }

        private void Controller_OnLifecycleFinished()
        {
            if (DateTime.Now - _lastTime > TimeSpan.FromMilliseconds(10))
            {
                _lastTime = DateTime.Now;
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TimeLabel.Text = controller.Context.CurrentTime.ToString();

                    foreach (var (plot, func) in _plots)
                    {
                        plot.AddPoint(func());
                    }

                    SimulationCanvas.Update(controller);
                });
            }
        }

        private void StartButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Controller.Paused = false;
            if (!Controller.IsRunning)
            {
                StartSimulation();
            }
        }

        private void PauseButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Controller.Paused = true;
        }

        private void StopButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Controller.Paused = false;
            Controller.IsRunning = false;
        }

        private void SleepTime_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            controller.SleepTime = (int)e.NewValue;
        }

        private void DeltaTime_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            controller.DeltaTime = (int)e.NewValue;
        }

        private void TilesOpacitySlider_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            SimulationCanvas.TileOpacity = TilesOpacitySlider.Value;
            SimulationCanvas.InvalidateVisual();
        }
    }
}
