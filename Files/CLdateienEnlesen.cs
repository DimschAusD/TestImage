using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestImage.Files
{
    internal class CLdateienEnlesen : IDateienEinlesen
    {
        public async IAsyncEnumerable<string> DateienEinlesenAsync(string verzeichnisPfad, bool unterverzeichnisseEinbeziehen = false, CancellationToken abbruchToken = default)
        {
            //throw new NotImplementedException();
            var suchOptions = new EnumerationOptions
            {
                RecurseSubdirectories = unterverzeichnisseEinbeziehen,
                IgnoreInaccessible = true
            };

            // Directory.EnumerateFiles ist speicherschonender als GetFiles
            var files = Directory.EnumerateFiles(verzeichnisPfad, "*.*", suchOptions);

            foreach (var file in files)
            {
                // Simuliert eine kurze Pause oder asynchrone Arbeit, 
                // damit das UI flüssig bleibt
                await Task.Yield();

                // Gibt die Datei (oder Zeile) sofort zurück
                yield return file;
            }

        }
    }
}
