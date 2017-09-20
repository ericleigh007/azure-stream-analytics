//********************************************************* 
// 
//    Copyright (c) Microsoft. All rights reserved. 
//    This code is licensed under the Microsoft Public License. 
//    THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF 
//    ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY 
//    IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR 
//    PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT. 
// 
//********************************************************* 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SensorEventGenerator
{
    public class Sensor
    {
        public string time;
        public string dspl;
        public int temp;
        public int hmdt;
        public int heartRate;
        public double runtimeSeconds;
        public double distanceSinceGoal;
        public bool shot;
        public int aggShot;
        public bool kill;
        public int aggKill;
        public bool iDied;
        public int aggiDied;
        public double distance;
        public double speed;
        public bool reachedGoal;
        public int goalsReached;

        private Random R;
        private double dieTime;
        public double goalDistance;

        static Random Rstat = new Random();
        static string[] sensorNames = new[] { "trainerA", "trainerB", "trainerC", "trainerD", "trainerE" };

        static Dictionary<string, Sensor> _sensors = new Dictionary<string, Sensor>();

        private DateTime startTime = DateTime.MinValue;

        public static Sensor Generate()
        {
            string sensorName = sensorNames[Rstat.Next(sensorNames.Length - 1)];

            Sensor thisTrainer;
            if (_sensors.ContainsKey(sensorName))
            {
                thisTrainer = _sensors[sensorName];
            }
            else
            {
                thisTrainer = new Sensor();
                _sensors.Add(sensorName, thisTrainer);
            }

            thisTrainer.dspl = sensorName;

            DateTime timeNow = DateTime.UtcNow;
            if (thisTrainer.startTime == DateTime.MinValue)
            {
                thisTrainer.startTime = timeNow;
            }

            if (thisTrainer.R == null)
            {
                thisTrainer.R = new Random();
            }

            Random Robj = thisTrainer.R;

            double runtimeSecs = (timeNow - thisTrainer.startTime).TotalSeconds;
            double deltaSecs = runtimeSecs - thisTrainer.runtimeSeconds;
            double mySpeed = ((Math.Sin(runtimeSecs / 100.0)) * 2.4) + 3.2;
            double incrDist = deltaSecs * mySpeed;

            bool weShot = Robj.Next(0, 100) > 80 ? true : false;
            bool weKill = weShot & Robj.Next(0, 100) > 80 ? true : false;

            bool iDied = runtimeSecs >= thisTrainer.dieTime;
            if (iDied)
            {
                thisTrainer.CacluateNewDieTime(runtimeSecs, Robj);
                thisTrainer.aggiDied++;
            }

            thisTrainer.aggShot += weShot ? 1 : 0;
            thisTrainer.aggKill += weKill ? 1 : 0;
            thisTrainer.aggiDied += iDied ? 1 : 0;

            thisTrainer.time = timeNow.ToString();
            thisTrainer.dspl = sensorName;
            thisTrainer.temp = Robj.Next(70, 150);
            thisTrainer.hmdt = Robj.Next(30, 70);
            thisTrainer.heartRate = (int)((Math.Sin(runtimeSecs / 58.0) * 60.0) + 120.0);
            thisTrainer.runtimeSeconds = runtimeSecs;
            thisTrainer.distanceSinceGoal += incrDist;
            thisTrainer.shot = weShot;
            thisTrainer.kill = weKill;
            thisTrainer.distance += incrDist;
            thisTrainer.speed = mySpeed;

            bool hitGoal = thisTrainer.distanceSinceGoal >= thisTrainer.goalDistance;
            if (hitGoal)
            {
                thisTrainer.CalculateNewGoalDistance(Robj);
                thisTrainer.distanceSinceGoal = 0.0;
                thisTrainer.goalsReached++;
            }

            thisTrainer.reachedGoal = hitGoal;

            return thisTrainer;
        }

        private void CalculateNewGoalDistance(Random r)
        {
            goalDistance = (double)r.Next(500, 700);
        }

        private void CacluateNewDieTime(double currentRunTime, Random r)
        {
            dieTime = currentRunTime + (double)r.Next(60, 100);
        }
    }
}
