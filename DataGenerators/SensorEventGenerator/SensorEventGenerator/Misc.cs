using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiscellaneousJunk
{
    static class Misc
    {
        #region Utilities

        [ThreadStatic]
        private static int _messageLevel = 0;

        [ThreadStatic]
        private static string _padString = "";

        [ThreadStatic]
        private static TraceWriter _log;

        [ThreadStatic]
        private static int _myId = -1;

        [ThreadStatic]
        private static int _myThread = -1;

        [ThreadStatic]
        private static List<string> _logMessages = null;

        public static void Initialize( TraceWriter log, int Id)
        {
            _log = log;
            _myId = Id;

            _myThread = Thread.CurrentThread.ManagedThreadId;

            _messageLevel = 0;
            _padString = "";

            _logMessages = new List<string>();
        }

        public static double ReportMemoryUsage()
        {
            double usage = 0.0;
            var process = Process.GetCurrentProcess();
            {
                usage = (double)(process.PrivateMemorySize64 / 1024 / 1024);
                ReportInfo($"  note: process using {usage:0.00}MB of memory");
            }

            return usage;
        }

        public static int PushMessageLevel()
        {
            _messageLevel++;
            for (int j = 0; j < _messageLevel; j++)
            {
                _padString += "   ";
            }

            return _messageLevel;
        }

        public static int PopMessageLevel()
        {
            _messageLevel--;
            for (int j = 0; j < _messageLevel; j++)
            {
                _padString += "   ";
            }

            return _messageLevel;
        }

        public static void ReportInfo(string message)
        {
            if (_log != null)
            {
                var msg = $"{_padString}{_myThread:000}: {_myId:000000}: {message.TrimStart()}";
                _logMessages.Add("-I- " + msg);
                _log.Info(msg);
            }
        }

        public static void ReportWarning(string message)
        {
            if (_log != null)
            {
                var msg = $"{_padString}{_myThread:000}: {_myId:000000}: {message.TrimStart()}";
                _logMessages.Add("-W- " + msg);
                _log.Warning(msg);
            }
        }

        public static void ReportError(string message)
        {
            if (_log != null)
            {
                var msg = $"{_padString}{_myThread:000}: {_myId:000000}: {message.TrimStart()}";
                _logMessages.Add("-E- " + msg);
                _log.Error(msg);
            }
        }

        public static string GetLogMessages()
        {
            return String.Join("\n", _logMessages);
        }

        #endregion
    }
}
