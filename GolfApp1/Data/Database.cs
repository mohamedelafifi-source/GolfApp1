using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using GolfApp1.Models;

namespace GolfApp1.Data
{
    internal sealed class Database : IDisposable
    {
        private readonly string _path;
        private SqliteConnection? _conn;

        public Database(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public async Task InitializeAsync()
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = _path }.ToString();
            _conn = new SqliteConnection(cs);
            await _conn.OpenAsync();

            // If table exists but contains a CHECK() on NumberOfPlayers, migrate to new schema without the CHECK.
            var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='Clubs';";
            var existing = await cmd.ExecuteScalarAsync() as string;

            if (!string.IsNullOrWhiteSpace(existing))
            {
                // If the existing CREATE TABLE includes a CHECK constraint on NumberOfPlayers, recreate table.
                if (existing.IndexOf("CHECK", StringComparison.OrdinalIgnoreCase) >= 0
                    && existing.IndexOf("NumberOfPlayers", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using var tx = _conn.BeginTransaction();
                    try
                    {
                        // Create new table without CHECK constraint
                        using var createNew = _conn.CreateCommand();
                        createNew.Transaction = tx;
                        createNew.CommandText = @"
CREATE TABLE IF NOT EXISTS Clubs_new (
    Id TEXT PRIMARY KEY,
    ShortName TEXT NOT NULL UNIQUE,
    LongName TEXT NOT NULL UNIQUE,
    NumberOfPlayers INTEGER NOT NULL DEFAULT 0
);";
                        await createNew.ExecuteNonQueryAsync();

                        // Copy existing data (preserve NumberOfPlayers when present)
                        using var copy = _conn.CreateCommand();
                        copy.Transaction = tx;
                        copy.CommandText = @"
INSERT INTO Clubs_new (Id, ShortName, LongName, NumberOfPlayers)
SELECT Id, ShortName, LongName, 
       CASE WHEN typeof(NumberOfPlayers) IN ('integer','real') THEN NumberOfPlayers ELSE 0 END
FROM Clubs;";
                        await copy.ExecuteNonQueryAsync();

                        // Drop old table and rename new
                        using var drop = _conn.CreateCommand();
                        drop.Transaction = tx;
                        drop.CommandText = "DROP TABLE Clubs;";
                        await drop.ExecuteNonQueryAsync();

                        using var rename = _conn.CreateCommand();
                        rename.Transaction = tx;
                        rename.CommandText = "ALTER TABLE Clubs_new RENAME TO Clubs;";
                        await rename.ExecuteNonQueryAsync();

                        await tx.CommitAsync();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { /* ignore */ }
                        throw;
                    }
                }

                // Ensure table exists (in case it didn't at all) and has the desired columns.
                using var ensure = _conn.CreateCommand();
                ensure.CommandText = @"
CREATE TABLE IF NOT EXISTS Clubs (
    Id TEXT PRIMARY KEY,
    ShortName TEXT NOT NULL UNIQUE,
    LongName TEXT NOT NULL UNIQUE,
    NumberOfPlayers INTEGER NOT NULL DEFAULT 0
);";
                await ensure.ExecuteNonQueryAsync();
            }
            else
            {
                // Table not present, create it with relaxed NumberOfPlayers definition.
                using var create = _conn.CreateCommand();
                create.CommandText = @"
CREATE TABLE IF NOT EXISTS Clubs (
    Id TEXT PRIMARY KEY,
    ShortName TEXT NOT NULL UNIQUE,
    LongName TEXT NOT NULL UNIQUE,
    NumberOfPlayers INTEGER NOT NULL DEFAULT 0
);";
                await create.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<Club>> GetAllClubsAsync()
        {
            var list = new List<Club>();
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT Id, ShortName, LongName, NumberOfPlayers FROM Clubs ORDER BY RowId;";
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new Club
                {
                    Id = rdr.GetString(0),
                    ShortName = rdr.GetString(1),
                    LongName = rdr.GetString(2),
                    NumberOfPlayers = rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3)
                });
            }
            return list;
        }

        // Upsert by Id (insert or replace)
        public async Task UpsertClubAsync(Club club)
        {
            if (club is null) throw new ArgumentNullException(nameof(club));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            using var tran = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tran;
            cmd.CommandText = @"
INSERT OR REPLACE INTO Clubs (Id, ShortName, LongName, NumberOfPlayers)
VALUES ($id, $short, $long, $players);";
            cmd.Parameters.AddWithValue("$id", club.Id);
            cmd.Parameters.AddWithValue("$short", club.ShortName);
            cmd.Parameters.AddWithValue("$long", club.LongName);
            cmd.Parameters.AddWithValue("$players", club.NumberOfPlayers);
            await cmd.ExecuteNonQueryAsync();
            await tran.CommitAsync();
        }

        // Convenience insert (returns success flag and optional error)
        public async Task<(bool Success, string? Error)> InsertClubAsync(Club club)
        {
            try
            {
                await UpsertClubAsync(club);
                return (true, null);
            }
            catch (SqliteException ex)
            {
                return (false, ex.Message);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void Dispose()
        {
            try { _conn?.Close(); } catch { /* ignore */ }
            _conn?.Dispose();
            _conn = null;
        }
    }
}
