using System;
using System.Collections.Generic;
using System.Linq;
using CitySimulation.Behaviour;
using CitySimulation.Control;
using CitySimulation.Entities;
using CitySimulation.Tools;
using CitySimulation.Ver1.Entity;

namespace CitySimulation
{
    public abstract class Controller
    {

        public const int TestLoopsCount = 100000;
        public static Controller Instance /*{ get; private set; }*/;

        public event Action OnLifecycleFinished = delegate {};
        public event Action OnFinished = delegate {};

        protected void CallOnLifecycleFinished()
        {
            OnLifecycleFinished();
        }

        protected void CallOnFinished()
        {
            OnFinished();
        }

        public City City;

        public Context Context;

        protected Logger Logger => Context.Logger;
        // public RouteTable Routes;

        // public static CityTime CurrentTime/* { get; private set; }*/ = new CityTime();
        // public Logger Logger;// { get; private set; }
        public VirusSpreadModule VirusSpreadModule;

        public List<Module> Modules = new List<Module>();

        public int DeltaTime = 5 * 60;
        public int SleepTime { get; set; } = 0;

        public static bool IsRunning { get; set; }
        public static bool Paused { get; set; }

        public static Random Random { get; set; } = new Random();

        public Controller()
        {
            if (Instance != null)
            {
                throw new Exception("Singleton already exists");
            }

            Instance = this;
        }


        // public int? RunAsync(int loopsCount)
        // {
        //     int? sessionId = Logger?.Start();
        //
        //     int split = 3;
        //
        //     AsyncModule<Facility>[] facilityControllers = new AsyncModule<Facility>[split];
        //     AsyncModule<Person>[] personControllers = new AsyncModule<Person>[split];
        //
        //     int delta = City.Persons.Count / split;
        //
        //     for (int i = 0; i < split - 1; i++)
        //     {
        //         personControllers[i] = new AsyncModule<Person>(City.Persons
        //             .Skip(i * delta)
        //             .Take(delta).ToList());
        //         facilityControllers[i] = new AsyncModule<Facility>(City.Facilities.Values
        //             .Skip(i * delta)
        //             .Take(delta).ToList());
        //     }
        //     personControllers[split - 1] = new AsyncModule<Person>(City.Persons.Skip((split - 1) * delta).ToList());
        //     facilityControllers[split - 1] = new AsyncModule<Facility>(City.Facilities.Values.Skip((split - 1) * delta).ToList());
        //
        //     Barrier barrier = new Barrier(personControllers.Length + facilityControllers.Length + 1);
        //
        //
        //
        //     foreach (var module in personControllers)
        //     {
        //         Task.Run(() => module.RunAsync(barrier));
        //     }
        //
        //     foreach (var module in facilityControllers)
        //     {
        //         Task.Run(() => module.RunAsync(barrier));
        //     }
        //
        //     for (int i = 0; i < loopsCount; i++)
        //     {
        //
        //         Logger?.PreProcess();
        //
        //         barrier.SignalAndWait();
        //
        //         Logger?.Process();
        //
        //         barrier.SignalAndWait();
        //
        //         Logger?.PostProcess();
        //
        //         barrier.SignalAndWait();
        //
        //         CurrentTime.AddMinutes(DeltaTime);
        //         OnLifecycleFinished();
        //         if (SleepTime != 0)
        //         {
        //             Thread.Sleep(SleepTime);
        //         }
        //
        //         barrier.SignalAndWait();
        //     }
        //     
        //     Logger?.Stop();
        //
        //     return sessionId;
        // }

        public abstract int? RunAsync(int threads);
        public abstract int? Run(int loopsCount);

        public abstract int? Run();

        protected void SetContextForAll(Context context)
        {
            City.Persons.ForEach(x=>x.Context = context);
            City.Facilities.Values.ToList().ForEach(x=>x.Context = context);
        }

        protected void PreRun()
        {
            IReadOnlyList<Facility> facilities = City.Facilities.Values.ToList();

            foreach (Person person in City.Persons)
            {
                person.Setup();
            }

            foreach (Facility facility in facilities)
            {
                facility.Setup();
            }

            //Кол-во работников может отличаться, так что устовим реальные значения
            foreach (Service service in City.Facilities.Values.OfType<Service>())
            {
                service.WorkersCount = City.Persons.Count(x => x.Behaviour is IPersonWithWork behaviour && behaviour.WorkPlace == service);
            }

            Modules.ForEach(x => x.Setup(this));
            Modules.ForEach(x => x.PreRun());

            City.Persons.ForEach(x => x.PreRun());
            City.Facilities.Values.ToList().ForEach(x => x.PreRun());

        }

        public void Setup(Action<int> progressCallback = null)
        {
            Context.CurrentTime = new CityTime();
            Context.Routes = City.Facilities.CreateRouteTable(progressCallback);
            IReadOnlyList<Facility> facilities = City.Facilities.Values.ToList();
            foreach (Transport bus in facilities.OfType<Transport>())
            {
                bus.SetupRoute(Context.Routes, facilities);
            }
        }
    }
}
