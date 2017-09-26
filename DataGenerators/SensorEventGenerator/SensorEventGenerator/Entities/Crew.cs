using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SensorEventGenerator
{
    class Crew
    {
        public string time;
        public string stringState;
        public string crewID;

        public string gunnerName;
        public string driverName;

        public double latitude;
        public double longitude;

        static Random Rstat = new Random();
        static string[] trainerNames = new[] { "trainerA", "trainerB", "trainerC", "trainerD" };

        static Dictionary<string, Crew> _crews = new Dictionary<string, Crew>();
        static List<string> _userList;

        Crew()
        {
            crewID = Guid.NewGuid().ToString();
            time = DateTime.UtcNow.ToString("O");
            stringState = "active";
            gunnerName = "gunner" + "_" + crewID;
            driverName = "driver" + "_" + crewID;
            // east coast 29.118204,-80.975194
            // west limit 29.116616,-81.130686
            latitude = 29.117144  + (Rstat.Next(-5,5) / 100.0);
            longitude = -81.043686 + (Rstat.Next(-5, 5) / 100.0);
        }
        public static void SetUserList(List<string> users)
        {
            _userList = users;
        }

        public static Crew Generate()
        {
            Crew newCrew = new Crew();
            _crews.Add(newCrew.crewID, newCrew);

            // find new crew members, BUT not ones already in other training systems

            return newCrew;
        }

        public bool Remove()
        {
            stringState = "inactive";
            time = DateTime.UtcNow.ToString("O");

            try
            {
                _crews.Remove(crewID);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public static Crew Remove( string key )
        {
            Crew removedCrew = null;
            try
            {
                removedCrew = _crews[key];
                _crews.Remove(key);

                removedCrew.stringState = "inactive";
                removedCrew.time = DateTime.UtcNow.ToString("O");

                return removedCrew;

            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}
