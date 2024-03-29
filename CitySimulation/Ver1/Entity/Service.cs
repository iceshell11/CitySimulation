﻿using CitySimulation.Behaviour.Action;
using CitySimulation.Entities;
using Range = CitySimulation.Tools.Range;

namespace CitySimulation.Ver1.Entity
{
    public class Service : Facility, IWorkplace
    {
        private static int _nextId;
        public static int GetId()
        {
            return _nextId++;
        }

        public Service(string name) : base(name)
        {
        }

        public Range WorkTime { get; set; }
        public int WorkersCount { get; set; }
        public int MaxWorkersCount { get; set; }

        public bool ForceAppointment { get; set; }
        public int VisitorsPerMonth { get; set; }
        public int VisitDuration { get; set; } = 30;

        public int VisitsCounter;
        public EntityAction BeginVisit(in int duration)
        {
            Context.Logger.LogVisit(this);
            return new ServiceVisiting(duration);
        }
    }

}
