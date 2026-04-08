using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace TestImage.Files
{
    internal interface  IDateienEinlesen
    {
        // Streamt die Dateien aus einem Verzeichnis ein
        IAsyncEnumerable<string> DateienEinlesenAsync(string verzeichnisPfad, bool unterverzeichnisseEinbeziehen=false, CancellationToken abbruchToken = default);
    }
}
