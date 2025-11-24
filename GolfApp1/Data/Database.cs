using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using GolfApp1.Models;

namespace GolfApp1.Data
{
    internal sealed partial class Database : IDisposable
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

            // Ensure Clubs table exists (no CHECK constraint)
            var createClubs = @"
CREATE TABLE IF NOT EXISTS Clubs (
    Id TEXT PRIMARY KEY,
    ShortName TEXT NOT NULL UNIQUE,
    LongName TEXT NOT NULL UNIQUE,
    NumberOfPlayers INTEGER NOT NULL DEFAULT 0
);";
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = createClubs;
            await cmd.ExecuteNonQueryAsync();

            // Ensure Players table exists
            var createPlayers = @"
CREATE TABLE IF NOT EXISTS Players (
    Id TEXT PRIMARY KEY,
    ClubShortName TEXT NOT NULL,
    Code TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL UNIQUE,
    IndexValue TEXT,
    Note TEXT,
    FOREIGN KEY(ClubShortName) REFERENCES Clubs(ShortName)
);";
            using var cmd2 = _conn.CreateCommand();
            cmd2.CommandText = createPlayers;
            await cmd2.ExecuteNonQueryAsync();
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

        public async Task<(bool Success, string? Error)> InsertPlayerAsync(Player player)
        {
            if (player is null) throw new ArgumentNullException(nameof(player));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = @"
INSERT INTO Players (Id, ClubShortName, Code, Name, IndexValue, Note)
VALUES ($id, $club, $code, $name, $index, $note);";
                cmd.Parameters.AddWithValue("$id", player.Id);
                cmd.Parameters.AddWithValue("$club", player.ClubShortName);
                cmd.Parameters.AddWithValue("$code", player.Code);
                cmd.Parameters.AddWithValue("$name", player.Name);
                cmd.Parameters.AddWithValue("$index", string.IsNullOrEmpty(player.IndexValue) ? (object)DBNull.Value : player.IndexValue);
                cmd.Parameters.AddWithValue("$note", string.IsNullOrEmpty(player.Note) ? (object)DBNull.Value : player.Note);
                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
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

        public async Task<List<Player>> GetPlayersByClubAsync(string clubShort)
        {
            var list = new List<Player>();
            if (_conn is null) return list;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT Id, ClubShortName, Code, Name, IndexValue, Note FROM Players WHERE ClubShortName = $club ORDER BY RowId;";
            cmd.Parameters.AddWithValue("$club", clubShort);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new Player
                {
                    Id = rdr.GetString(0),
                    ClubShortName = rdr.GetString(1),
                    Code = rdr.GetString(2),
                    Name = rdr.GetString(3),
                    IndexValue = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4),
                    Note = rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5)
                });
            }
            return list;
        }

        public void Dispose()
        {
            try { _conn?.Close(); } catch { /* ignore */ }
            _conn?.Dispose();
            _conn = null;
        }
    }
}
