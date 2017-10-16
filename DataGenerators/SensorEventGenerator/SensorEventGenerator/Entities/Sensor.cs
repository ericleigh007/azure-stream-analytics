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
        public string sessionStart;
        public string sessionEnd;
        public string stringState;
        public string dspl;
        public string crewID;
        public int zero = 0;           // eventually, thees may be queries
        public int top_percent = 100;  // 
        public int temp;
        public int hmdt;
        public int heartRate;
        public double runtimeSeconds;
        public double distanceSinceGoal;
        public int coll;
        public int aggColl;
        public double timeToShot;
        public double timeToHit;
        public double timeToKill;
        public double timeToDied;
        public int shot; 
        public int aggShot;
        public int hit;
        public int aggHit;
        public int kill;
        public int aggKill;
        public int iDied;
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
        static string[] trainerNames = new[] { "trainerA", "trainerB", "trainerC", "trainerD" };

        static Dictionary<string, Sensor> _sensors = new Dictionary<string, Sensor>();
        static List<string> _userList;

        private DateTime startTime = DateTime.MinValue;

        public static void SetUserList( List<string> users )
        {
            _userList = users;
        }

        public static Sensor Generate()
        {
            string sensorName = trainerNames[Rstat.Next(trainerNames.Length - 1)];
            // string sensorName = "trainerA";  // testing

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
            bool isIdle = false;

            thisTrainer.runtimeSeconds = runtimeSecs;
            if (thisTrainer.runtimeSeconds >= thisTrainer.resetTime)
            {
                thisTrainer.startTime = timeNow;
                thisTrainer.runtimeSeconds = 0.0;

                thisTrainer.CalculateNewIdleTime(Robj);
            }
            else if (thisTrainer.idleTimer > 0.0)
            {
                runtimeSecs = 0.0;
                thisTrainer.idleTimer -= deltaSecs;
                if (thisTrainer.idleTimer > 0.0)
                {
                    isIdle = true;
                }
                else
                {
                    isIdle = false;
                }
            }

            if ( isIdle )
            {
                thisTrainer.stringState = "idle";
                thisTrainer.InitializeTrainerStream();

                // return a default stream
                return thisTrainer;
            }

            if ( runtimeSecs == 0.0 )
            {
                thisTrainer.InitializeTrainerStream();

                thisTrainer.stringState = "reset";
                thisTrainer.crewID = Guid.NewGuid().ToString();

                thisTrainer.CalcuateNewDieTime(Robj);
                thisTrainer.CalculateNewGoalDistance(Robj);
                thisTrainer.CalculateNewResetTime();

                return thisTrainer;
            }
            else if ( runtimeSecs > 0.0)
            {
                thisTrainer.stringState = "running";
            }

            double mySpeed = ((Math.Sin(runtimeSecs / 100.0)) * 2.4) + 3.2;
            double incrDist = deltaSecs * mySpeed;

            int weShot = Robj.Next(0, 100) > 70 ? 1 : 0;
            int weHit = weShot > 0 && Robj.Next(0, 100) > 50 ? 1 : 0;
            int weKill = weShot > 0 && Robj.Next(0, 100) > 80 ? 1 : 0;

            if ((weShot > 0) && (thisTrainer.timeToShot == 0.0))
            {
                thisTrainer.timeToShot = runtimeSecs;
            }

            if ((weHit > 0) && (thisTrainer.timeToHit == 0.0))
            {
                thisTrainer.timeToHit = runtimeSecs;
            }

            if ( (weKill > 0) && ( thisTrainer.timeToKill == 0.0 ))
            {
                thisTrainer.timeToKill = runtimeSecs;
            }

            int iDied = runtimeSecs >= thisTrainer.dieTime ? 1 : 0;
            if (iDied == 1)
            {
                if ( thisTrainer.timeToDied == 0.0 )
                {
                    thisTrainer.timeToDied = runtimeSecs;
                }

                thisTrainer.CalcuateNewDieTime(Robj);
            }

            thisTrainer.shot = weShot;
            thisTrainer.hit = weHit;
            thisTrainer.kill = weKill;

            thisTrainer.aggShot += weShot > 0 ? 1 : 0;
            thisTrainer.aggHit += weHit > 0 ? 1 : 0;
            thisTrainer.aggKill += weKill > 0 ? 1 : 0;

            thisTrainer.iDied = iDied;
            thisTrainer.aggiDied += iDied == 1 ? 1 : 0;

            thisTrainer.time = timeNow.ToString("O");
            thisTrainer.dspl = sensorName;
            thisTrainer.temp = Robj.Next(70, 150);
            thisTrainer.hmdt = Robj.Next(30, 70);
            thisTrainer.heartRate = (int)((Math.Sin(runtimeSecs / 58.0) * 60.0) + 120.0);
            thisTrainer.incrDistance = incrDist;
            thisTrainer.distanceSinceGoal += incrDist;
            thisTrainer.distance += incrDist;
            thisTrainer.speed = mySpeed;
            thisTrainer.x_pos += 0.707 * incrDist;
            thisTrainer.z_pos += 0.707 * incrDist;
            thisTrainer.heading_deg = 45.0;  // yes, I know this is probably not matching the X/Z incrdistance, but we are just testing

            thisTrainer.coll = Robj.Next(0, 100) > 95 ? 1 : 0;
            thisTrainer.aggColl += thisTrainer.coll;

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
            runtimeSeconds = 0.0;

            x_pos = -1000.0;
            z_pos = -1000.0;

            distanceSinceGoal = 0.0;
            timeToShot = 0.0;
            timeToHit = 0.0;
            timeToKill = 0.0;
            timeToDied = 0.0;
            aggShot = 0;
            aggHit = 0;
            aggKill = 0;
            aggiDied = 0;
            coll = 0;
            aggColl = 0;
            distance = 0.0;
            goalsReached = 0;
            shot = 0;
            hit = 0;
            kill = 0;
            heartRate = 0;
            hmdt = 0;
            iDied = 0;
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
