using Microsoft.Data.Sqlite;

namespace Firepit.Knowledge.Store;

// Loads the sqlite-vec extension (`vec0`) into a SqliteConnection. The native
// binary ships via the `sqlite-vec` NuGet package — MSBuild drops it under
// `runtimes/{rid}/native/` adjacent to the executable, and .NET's native
// library resolver finds it at runtime. We just enable extension loading and
// ask SQLite for "vec0" — SQLite appends the platform suffix (.dll/.so/.dylib)
// to match what the package shipped. Ported from the-fishbowl.
internal static class SqliteVecLoader
{
    public static void LoadInto(SqliteConnection connection)
    {
        connection.EnableExtensions(true);
        try
        {
            connection.LoadExtension("vec0");
        }
        finally
        {
            // Flip extension loading back off once `vec0` is in. This is the
            // pattern Microsoft.Data.Sqlite docs recommend — keeps ad-hoc
            // `load_extension()` calls from untrusted SQL blocked for the rest
            // of the connection's life.
            connection.EnableExtensions(false);
        }
    }
}
