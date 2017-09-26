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
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using System.Configuration;
using Microsoft.SharePoint.Client;
using SP = Microsoft.SharePoint.Client;
using SharepointOnlineInterface;

namespace SensorEventGenerator
{
    class EventHubObserver : IObserver<Sensor>
    {
        private EventHubConfig _config;
        private EventHubClient _eventHubRTClient;
        private EventHubClient _eventHubInitClient;

        private Logger _logger;
                
        public EventHubObserver(EventHubConfig config)
        {
            try
            {
                _config = config;
                _eventHubRTClient = EventHubClient.CreateFromConnectionString(_config.ConnectionStringRT, config.EventHubNameRT);
                _eventHubInitClient = EventHubClient.CreateFromConnectionString(_config.ConnectionStringInit, config.EventHubNameInit);

                this._logger = new Logger(ConfigurationManager.AppSettings["logger_path"]);

                SharePointOnlineInterface.SetCredentials(_config.credsName, _config.credsKkey);

                var studentList = new List<string>();

                var listFrom = "Sharepoint";
                List<ListItem> lItems = null;

                Console.ForegroundColor = ConsoleColor.Magenta;

                try
                {
                    Console.WriteLine($"Attempting to load items from sharepoint");
                    lItems = SharePointOnlineInterface.GetAllItems(_config.studentSite, _config.studentList);
                    foreach (var it in lItems)
                    {
                        studentList.Add(it["Preferred_Name"].ToString());
                    }
                }
                catch (Exception ex)
                {
                    listFrom = "Internal default";
                    foreach (char a in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                    {
                        studentList.Add(a + " student");
                    }
                }

                Console.WriteLine($" Loaded student names from {listFrom}");

                Sensor.SetUserList(studentList);
            }
            catch (Exception ex)
            {
                _logger.Write(ex);
                throw ex;
            }

        }

        public void OnNext(Sensor sensorData)
        {
            try
            {
                if (sensorData.stringState == "running")
                {
                    var serialisedString = JsonConvert.SerializeObject(sensorData);
                    EventData data = new EventData(Encoding.UTF8.GetBytes(serialisedString)) { PartitionKey = sensorData.dspl };
                    _eventHubRTClient.Send(data);

                    //                    Console.ForegroundColor = ConsoleColor.Yellow;
                    //                    Console.WriteLine("Sending" + serialisedString + " at: " + sensorData.time);

                    if (sensorData.shot > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($" >>> Trainer {sensorData.dspl} shot");
                    }

                    if (sensorData.kill > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($" >>>>>>> Trainer {sensorData.dspl} killed a target");
                    }

                    if (sensorData.reachedGoal)
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($" >>>>>>>>>> Trainer {sensorData.dspl} reached a goal");
                    }

                    if (sensorData.iDied > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  ******** Trainer {sensorData.dspl} died");
                    }

                    //To write every event entry to the logfile, uncomment the line below. 
                    //Note: Writing every event can quickly grow the size of the log file.
                    //_logger.Write("Sending" + serialisedString + " at: " + sensorData.TimeStamp);
                }
                else if (sensorData.stringState == "reset")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  &&&&&&&&& Trainer {sensorData.dspl} RESET ");

                    string serializedString = "";
                    EventData data;

                    var removedCrew = Crew.Remove(sensorData.crewID);
                    if (removedCrew != null)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($" removed crew {sensorData.crewID}");

                        serializedString = JsonConvert.SerializeObject(removedCrew);
                        data = new EventData(Encoding.UTF8.GetBytes(serializedString)) { PartitionKey = removedCrew.crewID };
                        _eventHubInitClient.Send(data);
                    }

                    var crewData = Crew.Generate();

                    Console.WriteLine($"  ++++++++++ Created crew {crewData.crewID} at {crewData.latitude}/{crewData.longitude} on {crewData.time} and assigned to Trainer {sensorData.dspl}");

                    serializedString = JsonConvert.SerializeObject(crewData);
                    data = new EventData(Encoding.UTF8.GetBytes(serializedString)) { PartitionKey = crewData.crewID };
                    _eventHubInitClient.Send(data);
                }
                else if ( sensorData.stringState == "idle")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  zzzzzzzzz Trainer {sensorData.dspl} is currently idle and not sending");
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"  {sensorData.dspl} at {sensorData.time} : {sensorData.runtimeSeconds}");
            }
            catch (Exception ex)
            {
                _logger.Write(ex);
                throw ex;
            }

        }

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            _logger.Write(error);
            throw error;
        }

    }
}
