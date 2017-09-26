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
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

namespace SensorEventGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new EventHubConfig();
                  
            
            // Uncomment for picking from Configuration 
            config.ConnectionStringRT = ConfigurationManager.AppSettings["EventHubConnectionStringRT"];
            config.EventHubNameRT = ConfigurationManager.AppSettings["EventHubRTName"];
            config.ConnectionStringInit = ConfigurationManager.AppSettings["EventHubConnectionStringInit"];
            config.EventHubNameInit = ConfigurationManager.AppSettings["EventHubInitName"];
            config.credsName = ConfigurationManager.AppSettings["credsName"];
            config.credsKkey = ConfigurationManager.AppSettings["credsKey"];
            config.studentSite = ConfigurationManager.AppSettings["studentSite"];
            config.studentList = ConfigurationManager.AppSettings["studentList"];
                        
            //To push 3 event per second
            var eventHubevents = Observable.Interval(TimeSpan.FromSeconds(.3)).Select(i => Sensor.Generate());

            //To send Data to EventHub as JSON
            var eventHubDis = eventHubevents.Subscribe(new EventHubObserver(config));
                                
            Console.ReadLine();
            eventHubDis.Dispose();
                   
	
        }
    }
}
