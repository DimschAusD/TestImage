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


            // Restzeit aus der TATSÄCHLICHEN Rate, also StückPerSecond.
            //
            // Hier stand StückGesammt / secs — die Geschwindigkeit, die man hätte, wenn
            // schon alles fertig wäre. Bei 1000 Stück und 10 geschafften nach 10 Sekunden
            // ergab das 100/s statt 1/s und damit 9,9 statt 990 Sekunden Restzeit. Der
            // Fehler war am Anfang eines Laufs am grössten, also genau dann, wenn man auf
            // die Zahl schaut.
            Restzeit = StückPerSecond > 0
                ? FormatiereDauer((StückGesammt - StückAbgearbeitet) / StückPerSecond)
                : "—";
        }

        /// <summary>
        /// Sekunden lesbar machen. „500,0 Sek" sagt weniger als „8:20 Min" — und bei
        /// tausenden Bildern kommen solche Zeiten regelmässig vor.
        /// </summary>
        private static string FormatiereDauer(double sekunden)
        {
            if (double.IsNaN(sekunden) || double.IsInfinity(sekunden) || sekunden < 0)
            {
                return "—";
            }

            if (sekunden < 60)
            {
                return $"{sekunden:F0} Sek";
            }

            var spanne = TimeSpan.FromSeconds(sekunden);

            return spanne.TotalHours >= 1
                ? $"{(int)spanne.TotalHours}:{spanne.Minutes:D2}:{spanne.Seconds:D2} Std"
                : $"{spanne.Minutes}:{spanne.Seconds:D2} Min";
        }

       
      
       
    }
}
