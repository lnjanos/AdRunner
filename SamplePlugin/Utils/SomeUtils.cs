using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdRunner.Utils
{
    internal class Utils
    {
        public static unsafe string ReadStringFromPointer(byte* ptr)
        {
            if (ptr == null)
                return "(null)";
            // Nullterminierten UTF-8-String in C#-String konvertieren
            return Encoding.UTF8.GetString(
                System.Runtime.InteropServices.MemoryMarshal
                    .CreateReadOnlySpanFromNullTerminated(ptr)
            );
        }

    }
}
