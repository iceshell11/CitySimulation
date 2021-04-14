﻿using System;
using System.Collections.Generic;
using System.Linq;
using CitySimulation.Entity;
using CitySimulation.Tools;
using Range = CitySimulation.Tools.Range;

namespace CitySimulation.Generation.Models
{
    public class ServicesConfig
    {
        public abstract class ServiceDataBase
        {
            public string Prefix { get; set; } = "";
            public Type Type { get; set; }
            public int FamiliesPerService { get; set; }
            public Range WorkersPerService { get; set; }
            public TimeRange WorkerTime { get; set; }
            public string[] Labels { get; set; }
            public abstract Service Create(string name);
        }
        public class ServiceData<T> : ServiceDataBase where T : Service
        {
            public ServiceData()
            {
            }

            public ServiceData(params string[] labels)
            {
                Labels = labels;
            }

            public override Service Create(string name)
            {
                return (Service)typeof(T).GetConstructor(new[] {typeof(string)}).Invoke(new object[]{ Prefix + name });
            }
        }

        public List<ServiceDataBase> ServicesData { get; set; } = new List<ServiceDataBase>();
        public float LocalWorkersRatio { get; set; }
        public float ServiceWorkersRatio { get; set; }

        public List<Service> GenerateServices(int familiesCount, int size)
        {
            List<Service> res = new List<Service>();


            foreach (ServiceDataBase data in ServicesData)
            {
                int servicesCount = familiesCount / data.FamiliesPerService;
                for (int i = 0; i < servicesCount; i++)
                {
                    var service = data.Create(Service.GetId().ToString());
                    service.WorkersCount = data.WorkersPerService.Random(Controller.Random);
                    service.MaxWorkersCount = data.WorkersPerService.End;
                    service.WorkTime = data.WorkerTime;
                    service.Size = new Point(size, size);

                    res.Add(service);
                }
            }

            // int servicesCount = familiesCount / FamiliesPerBasicService;
            //
            // for (int i = 0; i < servicesCount; i++)
            // {
            //     res.Add(new Service("MinService" + Service.GetId())
            //     {
            //         WorkersCount = WorkersPerBasicService.Random(Controller.Random),
            //         WorkTime = new TimeRange(8 * 60, 18 * 60),
            //         Size = new Point(size, size)
            //     });
            // }
            //
            //
            // int storesCount = familiesCount / FamiliesPerStore;
            //
            // for (int i = 0; i < storesCount; i++)
            // {
            //     res.Add(new Service("Store" + Service.GetId())
            //     {
            //         WorkersCount = WorkersPerStore.Random(Controller.Random),
            //         WorkTime = new TimeRange(8 * 60, 18 * 60),
            //         Size = new Point(size, size)
            //     });
            // }

            return res;
        }
    }
}