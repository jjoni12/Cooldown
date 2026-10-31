using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Cooldown
{
    static class Sqlite
    {
        const int SQLITE_OK = 0, SQLITE_ROW = 100;
        const int SQLITE_OPEN_READONLY = 0x1, SQLITE_OPEN_URI = 0x40;

        [DllImport("winsqlite3.dll")] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll")] static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_close(IntPtr db);

        static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        public static string ReadText(string path, string sql, string parameter)
        {
            try
            {
                return Read(path, "mode=ro", sql, parameter);
            }
            catch (InvalidOperationException)
            {
                // A WAL database can't be opened read-only while its -shm file is missing (its app is closed).
                return Read(path, "immutable=1", sql, parameter);
            }
        }

        static string Read(string path, string options, string sql, string parameter)
        {
            var uri = "file:///" + path.Replace('\\', '/').Replace("%", "%25").Replace("?", "%3f").Replace("#", "%23") + "?" + options;
            if (sqlite3_open_v2(Utf8(uri), out var db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI, IntPtr.Zero) != SQLITE_OK)
            {
                sqlite3_close(db);
                throw new InvalidOperationException("can't open " + path);
            }
            try
            {
                sqlite3_busy_timeout(db, 2000);
                if (sqlite3_prepare_v2(db, Utf8(sql), -1, out var statement, IntPtr.Zero) != SQLITE_OK)
                    throw new InvalidOperationException("can't read " + path);
                try
                {
                    var value = Encoding.UTF8.GetBytes(parameter);
                    sqlite3_bind_text(statement, 1, value, value.Length, SQLITE_TRANSIENT);
                    if (sqlite3_step(statement) != SQLITE_ROW) return null;
                    var length = sqlite3_column_bytes(statement, 0);
                    if (length <= 0) return "";
                    var bytes = new byte[length];
                    Marshal.Copy(sqlite3_column_blob(statement, 0), bytes, 0, length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    sqlite3_finalize(statement);
                }
            }
            finally
            {
                sqlite3_close(db);
            }
        }

        static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
    }
}
