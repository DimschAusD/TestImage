using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TestImage
{
    internal class CLProgressStückzahl
    {
        public DateTime Started { get; }
        public TimeSpan RunTime { get; }
        public long StückGesammt { get; }
        public long StückAbgearbeitet { get; }
        public double Percent { get; }
        public double StückPerSecond { get; }
        public string Speed { get; }
        public bool Done { get; set; }

        public string StückRest { get; }

        public string Restzeit {  get; }

        public CLProgressStückzahl(DateTime started,  long stückGesammt, long stückAbgearbeitet,  bool done)
        {
            Started = started;
            RunTime = DateTime.Now - Started;
            StückGesammt = stückGesammt;
            StückAbgearbeitet = stückAbgearbeitet;
            //Percent = percent;
            //StückPerSecond = stückPerSecond;
            //Speed = speed;
            Done = done;
            StückRest = "stückRest";


            Percent = StückGesammt > 0 ? StückAbgearbeitet / (double)StückGesammt * 100D : 0;

            if (StückGesammt > 0)
            {
                try
                {
                    double secs = RunTime.TotalSeconds;
                    if ((secs > 0) & (stückAbgearbeitet > 0))
                    {
                        StückPerSecond = StückAbgearbeitet / secs;
                    }
                }
                catch { }
            }

            if (StückPerSecond > 0)
            {
                double val = StückPerSecond;
                //string[] exts = new string[] { "B", "Kb", "Mb", "Gb", "Tb", "Pb", "Xb", "Yb", "Zb" };
                //int index = 0;
                //while (val >= 1024)
                //{
                //    val /= 1024;
                //    index++;
                //    if (index >= exts.Length - 1)
                //    {
                //        break;
                //    }
                //}
                Speed = $"{val:0.00} Stk/s";
                if (stückGesammt >= stückAbgearbeitet)
                {
                    StückRest = $"{(stückGesammt - stückAbgearbeitet):0.00}";
                }

            }
            else
            {
                Speed = "0 B/s";
            }


            // Restzeit
            try
            {
                double secs = RunTime.TotalSeconds;
                var proSek = StückGesammt / secs;
                Restzeit = ((StückGesammt - StückAbgearbeitet) / proSek).ToString("F1") + " Sek";

            }
            catch
            {
                Restzeit = "-1";
            }

        }

       
      
       
    }
}
