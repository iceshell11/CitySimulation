﻿using System;
using System.Collections.Generic;
using System.Text;

namespace CitySimulation
{
    public struct HealthData
    {
        public HealthStatus HealthStatus;
        public int StatusTimer;

        public bool Infected => HealthStatus == HealthStatus.InfectedSpread || HealthStatus == HealthStatus.InfectedIncubation;

        private const int IncubTime = 60 * 24;
        private const int SpreadTime = 60 * 24;
        private const int ImmuneTime = 60 * 24;

        public void Process()
        {
            if (StatusTimer != 0)
            {
                StatusTimer -= Controller.Instance.DeltaTime;
                if (StatusTimer == 0)
                {
                    switch (HealthStatus)
                    {
                        case HealthStatus.InfectedIncubation:
                            HealthStatus = HealthStatus.InfectedSpread;
                            StatusTimer = (1 + Controller.CurrentTime.Minutes % 7) * SpreadTime;
                            break;
                        case HealthStatus.InfectedSpread:
                            HealthStatus = HealthStatus.Immune;
                            StatusTimer = (1 + Controller.CurrentTime.Minutes % 7) * ImmuneTime;
                            break;
                        case HealthStatus.Immune:
                            HealthStatus = HealthStatus.Default;
                            break;
                    }
                }
            }
        }

        public bool TryInfect()
        {
            if (HealthStatus == HealthStatus.Default)
            {
                HealthStatus = HealthStatus.InfectedIncubation;
                StatusTimer = (1 + Controller.CurrentTime.Minutes % 7) * IncubTime;
                return true;
            }

            return false;
        }
    }

    public enum HealthStatus
    {
        Default,
        InfectedIncubation,
        InfectedSpread,
        Immune
    }
}
