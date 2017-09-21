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
        /* 
            Distance traveled 
            Fuel used/savings 
            Time to pass checkpoints  
            Average speed 
            Top speed 
            Actual speed vs. recommended speed  // where does recommended speed come from?
            Collisions 
 
            Force on Force (Raydon booth) 
            Show that we can set up, monitor, and report on objective 
            5 hits = kill  
            Objective is not only to kill but also avoid being killed  
            Scoring could take into account evasion & accuracy (aka how many rounds were fired at you by enemy without killing you)  
            Data collected and displayed:  
            Rounds fired at you by enemy without killing you 
            Who won 
            Kills  
            Deaths  
            Kill/death ratio  
            Number of shots fired  
            Hits and hit percentage (hits out of rounds fired)  
            Average speed  
            Top speed  
            Number of collisions  
            Distance traveled  
            How did the Players do over time with kills? (improvement)  
            How does my team compare to other teams who have played?  (last time, today, overall)  
            Data collected but not displayed:   
            How much time was spent in various areas?  (heatmap)  
            Team size (number of vehicles and number of players per vehicle)  
            Usage over time (hour, day, etc.)   // only stream from trainer when it is started.  Then we can query for this
            Time between runs/idle time  
            Duration of scenario    // average maximum runtime
        */
        public string time;
        public string dspl;
        public int zero = 0;           // eventually, thees may be queries
        public int top_percent = 100;  // 
        public int temp;
        public int hmdt;
        public int heartRate;
        public double runtimeSeconds;
        public double distanceSinceGoal;
        public double timeToKill;
        public bool shot; 
        public int aggShot;
        public bool kill;
        public int aggKill;
        public bool iDied;
        public int aggiDied;
        public double x_pos;
        public double z_pos;
        public double heading_deg;
        public double incrDistance;
        public double distance;
        public double speed;
        public bool reachedGoal;
        public int goalsReached;
        public double goalDistance;

        private Random R;
        private double dieTime = 10000.0;
        private double resetTime = 10000.0;
        private double idleTimer = 0.0;

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

            if (thisTrainer.runtimeSeconds >= thisTrainer.resetTime)
            {
                thisTrainer.startTime = timeNow;
                thisTrainer.runtimeSeconds = 0.0;

                thisTrainer.CalculateNewIdleTime(Robj);

                //// hakck -- this is not working yet.. hack hack....
            }

            double runtimeSecs = (timeNow - thisTrainer.startTime).TotalSeconds;

            double deltaSecs = runtimeSecs - thisTrainer.runtimeSeconds;
            if ( runtimeSecs < 0.1 )
            {
                thisTrainer.InitializeTrainerStream();
            }

            double mySpeed = ((Math.Sin(runtimeSecs / 100.0)) * 2.4) + 3.2;
            double incrDist = deltaSecs * mySpeed;

            bool weShot = Robj.Next(0, 100) > 80 ? true : false;
            bool weKill = weShot & Robj.Next(0, 100) > 80 ? true : false;

            bool iDied = runtimeSecs >= thisTrainer.dieTime;
            if (iDied)
            {
                thisTrainer.CalcuateNewDieTime(Robj);
            }

            thisTrainer.aggShot += weShot ? 1 : 0;
            thisTrainer.aggKill += weKill ? 1 : 0;

            thisTrainer.iDied = iDied;
            thisTrainer.aggiDied += iDied ? 1 : 0;

            thisTrainer.time = timeNow.ToString();
            thisTrainer.dspl = sensorName;
            thisTrainer.temp = Robj.Next(70, 150);
            thisTrainer.hmdt = Robj.Next(30, 70);
            thisTrainer.heartRate = (int)((Math.Sin(runtimeSecs / 58.0) * 60.0) + 120.0);
            thisTrainer.runtimeSeconds = runtimeSecs;
            thisTrainer.incrDistance = incrDist;
            thisTrainer.distanceSinceGoal += incrDist;
            thisTrainer.shot = weShot;
            thisTrainer.kill = weKill;
            thisTrainer.distance += incrDist;
            thisTrainer.speed = mySpeed;
            thisTrainer.x_pos += 0.707 * incrDist;
            thisTrainer.z_pos += 0.707 * incrDist;
            thisTrainer.heading_deg = 45.0;  // yes, I know this is probably not matching the X/Z incrdistance, but we are just testing

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

        private void InitializeTrainerStream()
        {
            x_pos = -1000.0;
            z_pos = -1000.0;

            distanceSinceGoal = 0.0;
            timeToKill = 0.0;
            aggShot = 0;
            aggKill = 0;
            aggiDied = 0;
            distance = 0.0;
            goalsReached = 0;

            CalcuateNewDieTime(R);
            CalculateNewGoalDistance(R);
            CalculateNewResetTime();
        }

        private void CalculateNewResetTime()
        {
            resetTime = runtimeSeconds + (double)R.Next(150, 210);
        }

        private void CalculateNewIdleTime(Random r)
        {
            idleTimer = (double)R.Next(20, 30);
        }

        private void CalculateNewGoalDistance(Random r)
        {
            goalDistance = (double)r.Next(500, 700);
        }

        private void CalcuateNewDieTime(Random r)
        {
            dieTime = runtimeSeconds + (double)r.Next(60, 100);
        }
    }
}
