//Database.cs
//==============
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

            // Ensure foreign key enforcement is enabled for this connection.
            // SQLite requires PRAGMA foreign_keys = ON per connection.
            using (var fkCmd = _conn.CreateCommand())
            {
                fkCmd.CommandText = "PRAGMA foreign_keys = ON;";
                await fkCmd.ExecuteNonQueryAsync();
            }

            // Clubs
            var createClubs = @"
CREATE TABLE IF NOT EXISTS Clubs (
    Id TEXT PRIMARY KEY,
    ShortName TEXT NOT NULL UNIQUE,
    LongName TEXT NOT NULL UNIQUE,
    NumberOfPlayers INTEGER NOT NULL DEFAULT 0
);";
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = createClubs;
                await cmd.ExecuteNonQueryAsync();
            }

            // Players
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
            using (var cmd2 = _conn.CreateCommand())
            {
                cmd2.CommandText = createPlayers;
                await cmd2.ExecuteNonQueryAsync();
            }

            // Results
            var createResults = @"
CREATE TABLE IF NOT EXISTS Results (
    Id TEXT PRIMARY KEY,
    Date TEXT NOT NULL,
    ClubShortName TEXT NOT NULL,
    Venue TEXT,
    PlayerId TEXT,
    PartnerId TEXT,
    PlayerName TEXT,
    PartnerName TEXT,
    Hcp INTEGER,
    Score INTEGER,
    Position INTEGER,
    FOREIGN KEY(PlayerId) REFERENCES Players(Id),
    FOREIGN KEY(PartnerId) REFERENCES Players(Id)
);";
            using (var cmd3 = _conn.CreateCommand())
            {
                cmd3.CommandText = createResults;
                await cmd3.ExecuteNonQueryAsync();
            }

            using (var idxCmd = _conn.CreateCommand())
            {
                idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Results_Club_Date ON Results(ClubShortName, Date);";
                await idxCmd.ExecuteNonQueryAsync();
            }
        }

        // ============================================================================
        // CLUBS
        // ============================================================================

        public async Task<List<Club>> GetAllClubsAsync()
        {
            var list = new List<Club>();
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT c.Id, c.ShortName, c.LongName,
       COUNT(p.Id) AS NumberOfPlayers
FROM Clubs c
LEFT JOIN Players p ON p.ClubShortName = c.ShortName
GROUP BY c.Id, c.ShortName, c.LongName
ORDER BY c.ShortName;";
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

        public async Task<string?> DeleteClubAsync(string clubShort)
        {
            if (string.IsNullOrWhiteSpace(clubShort)) throw new ArgumentNullException(nameof(clubShort));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                // check player count
                using var cntCmd = _conn.CreateCommand();
                cntCmd.CommandText = "SELECT COUNT(1) FROM Players WHERE ClubShortName = $club;";
                cntCmd.Parameters.AddWithValue("$club", clubShort);
                var scalar = await cntCmd.ExecuteScalarAsync();
                var count = scalar == null || scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);

                if (count > 0)
                {
                    return $"Club '{clubShort}' has {count} players. Remove players first or delete them before deleting the club.";
                }

                using var tran = _conn.BeginTransaction();
                using var delCmd = _conn.CreateCommand();
                delCmd.Transaction = tran;
                delCmd.CommandText = "DELETE FROM Clubs WHERE ShortName = $club;";
                delCmd.Parameters.AddWithValue("$club", clubShort);
                await delCmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ============================================================================
        // PLAYERS
        // ============================================================================

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

                using var incCmd = _conn.CreateCommand();
                incCmd.Transaction = tran;
                incCmd.CommandText = "UPDATE Clubs SET NumberOfPlayers = NumberOfPlayers + 1 WHERE ShortName = $club;";
                incCmd.Parameters.AddWithValue("$club", player.ClubShortName);
                await incCmd.ExecuteNonQueryAsync();

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

        public async Task<string?> UpsertPlayerAsync(Player player)
        {
            if (player is null) throw new ArgumentNullException(nameof(player));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();

                using var checkCmd = _conn.CreateCommand();
                checkCmd.Transaction = tran;
                checkCmd.CommandText = "SELECT ClubShortName FROM Players WHERE Id = $id LIMIT 1;";
                checkCmd.Parameters.AddWithValue("$id", player.Id);
                var scalar = await checkCmd.ExecuteScalarAsync();
                string? existingClub = scalar == null || scalar == DBNull.Value ? null : Convert.ToString(scalar);

                if (existingClub is null)
                {
                    using var insertCmd = _conn.CreateCommand();
                    insertCmd.Transaction = tran;
                    insertCmd.CommandText = @"
INSERT INTO Players (Id, ClubShortName, Code, Name, IndexValue, Note)
VALUES ($id, $club, $code, $name, $index, $note);";
                    insertCmd.Parameters.AddWithValue("$id", player.Id);
                    insertCmd.Parameters.AddWithValue("$club", player.ClubShortName);
                    insertCmd.Parameters.AddWithValue("$code", player.Code);
                    insertCmd.Parameters.AddWithValue("$name", player.Name);
                    insertCmd.Parameters.AddWithValue("$index", string.IsNullOrEmpty(player.IndexValue) ? (object)DBNull.Value : player.IndexValue);
                    insertCmd.Parameters.AddWithValue("$note", string.IsNullOrEmpty(player.Note) ? (object)DBNull.Value : player.Note);
                    await insertCmd.ExecuteNonQueryAsync();

                    using var incCmd = _conn.CreateCommand();
                    incCmd.Transaction = tran;
                    incCmd.CommandText = "UPDATE Clubs SET NumberOfPlayers = NumberOfPlayers + 1 WHERE ShortName = $club;";
                    incCmd.Parameters.AddWithValue("$club", player.ClubShortName);
                    await incCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    using var updateCmd = _conn.CreateCommand();
                    updateCmd.Transaction = tran;
                    updateCmd.CommandText = @"
UPDATE Players
SET ClubShortName = $club, Code = $code, Name = $name, IndexValue = $index, Note = $note
WHERE Id = $id;";
                    updateCmd.Parameters.AddWithValue("$id", player.Id);
                    updateCmd.Parameters.AddWithValue("$club", player.ClubShortName);
                    updateCmd.Parameters.AddWithValue("$code", player.Code);
                    updateCmd.Parameters.AddWithValue("$name", player.Name);
                    updateCmd.Parameters.AddWithValue("$index", string.IsNullOrEmpty(player.IndexValue) ? (object)DBNull.Value : player.IndexValue);
                    updateCmd.Parameters.AddWithValue("$note", string.IsNullOrEmpty(player.Note) ? (object)DBNull.Value : player.Note);
                    await updateCmd.ExecuteNonQueryAsync();

                    if (!string.Equals(existingClub, player.ClubShortName, StringComparison.Ordinal))
                    {
                        using var decCmd = _conn.CreateCommand();
                        decCmd.Transaction = tran;
                        decCmd.CommandText = "UPDATE Clubs SET NumberOfPlayers = NumberOfPlayers - 1 WHERE ShortName = $club;";
                        decCmd.Parameters.AddWithValue("$club", existingClub);
                        await decCmd.ExecuteNonQueryAsync();

                        using var incCmd = _conn.CreateCommand();
                        incCmd.Transaction = tran;
                        incCmd.CommandText = "UPDATE Clubs SET NumberOfPlayers = NumberOfPlayers + 1 WHERE ShortName = $club;";
                        incCmd.Parameters.AddWithValue("$club", player.ClubShortName);
                        await incCmd.ExecuteNonQueryAsync();
                    }
                }

                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<List<Player>> GetPlayersByClubAsync(string clubShort)
        {
            var list = new List<Player>();
            if (_conn is null) return list;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT Id, ClubShortName, Code, Name, IndexValue, Note FROM Players WHERE ClubShortName = $club ORDER BY Code;";
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

        public async Task<string?> DeletePlayerAsync(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentNullException(nameof(playerId));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();

                using var clubCmd = _conn.CreateCommand();
                clubCmd.Transaction = tran;
                clubCmd.CommandText = "SELECT ClubShortName FROM Players WHERE Id = $id LIMIT 1;";
                clubCmd.Parameters.AddWithValue("$id", playerId);
                var scalar = await clubCmd.ExecuteScalarAsync();
                var club = scalar == null || scalar == DBNull.Value ? null : Convert.ToString(scalar);

                using (var delResultsCmd = _conn.CreateCommand())
                {
                    delResultsCmd.Transaction = tran;
                    delResultsCmd.CommandText = "DELETE FROM Results WHERE PlayerId = $id OR PartnerId = $id;";
                    delResultsCmd.Parameters.AddWithValue("$id", playerId);
                    await delResultsCmd.ExecuteNonQueryAsync();
                }

                using var delCmd = _conn.CreateCommand();
                delCmd.Transaction = tran;
                delCmd.CommandText = "DELETE FROM Players WHERE Id = $id;";
                delCmd.Parameters.AddWithValue("$id", playerId);
                await delCmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrEmpty(club))
                {
                    using var decCmd = _conn.CreateCommand();
                    decCmd.Transaction = tran;
                    decCmd.CommandText = "UPDATE Clubs SET NumberOfPlayers = NumberOfPlayers - 1 WHERE ShortName = $club;";
                    decCmd.Parameters.AddWithValue("$club", club);
                    await decCmd.ExecuteNonQueryAsync();
                }

                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<string?> DeleteResultsByPlayerIdAsync(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentNullException(nameof(playerId));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = "DELETE FROM Results WHERE PlayerId = $id OR PartnerId = $id;";
                cmd.Parameters.AddWithValue("$id", playerId);
                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<string?> RecomputeClubPlayerCountsAsync()
        {
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = @"
UPDATE Clubs
SET NumberOfPlayers = (
    SELECT COUNT(1) FROM Players WHERE Players.ClubShortName = Clubs.ShortName
);";
                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<int> GetResultsCountByPlayerIdAsync(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentNullException(nameof(playerId));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM Results WHERE PlayerId = $id OR PartnerId = $id;";
                cmd.Parameters.AddWithValue("$id", playerId);
                var scalar = await cmd.ExecuteScalarAsync();
                return scalar == null || scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
            }
            catch (SqliteException)
            {
                return -1;
            }
        }

        // ============================================================================
        // RESULTS
        // ============================================================================

        public async Task<string?> UpsertResultAsync(Models.ResultRecord r)
        {
            if (r is null) throw new ArgumentNullException(nameof(r));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = @"
INSERT OR REPLACE INTO Results
(Id, Date, ClubShortName, Venue, PlayerId, PartnerId, PlayerName, PartnerName, Hcp, Score, Position)
VALUES ($id, $date, $club, $venue, $playerId, $partnerId, $playerName, $partnerName, $hcp, $score, $position);";

                var id = string.IsNullOrEmpty(r.Id) ? Guid.NewGuid().ToString() : r.Id;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$date", r.Date.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$club", r.Club);
                cmd.Parameters.AddWithValue("$venue", string.IsNullOrEmpty(r.Venue) ? (object)DBNull.Value : r.Venue);
                cmd.Parameters.AddWithValue("$playerId", string.IsNullOrEmpty(r.PlayerId) ? (object)DBNull.Value : r.PlayerId);
                cmd.Parameters.AddWithValue("$partnerId", string.IsNullOrEmpty(r.PartnerId) ? (object)DBNull.Value : r.PartnerId);
                cmd.Parameters.AddWithValue("$playerName", string.IsNullOrEmpty(r.PlayerName) ? (object)DBNull.Value : r.PlayerName);
                cmd.Parameters.AddWithValue("$partnerName", string.IsNullOrEmpty(r.Partner) ? (object)DBNull.Value : r.Partner);
                cmd.Parameters.AddWithValue("$hcp", r.Hcp);
                cmd.Parameters.AddWithValue("$score", r.Result);
                cmd.Parameters.AddWithValue("$position", r.Position);

                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();

                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<List<Models.ResultRecord>> GetResultsAsync(string clubShort, DateTime? from = null, DateTime? to = null)
        {
            var list = new List<Models.ResultRecord>();
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            var sql = "SELECT Id, Date, ClubShortName, Venue, PlayerId, PartnerId, PlayerName, PartnerName, Hcp, Score, Position FROM Results WHERE ClubShortName = $club";
            cmd.Parameters.AddWithValue("$club", clubShort);

            if (from is not null)
            {
                sql += " AND Date >= $from";
                cmd.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd"));
            }
            if (to is not null)
            {
                sql += " AND Date <= $to";
                cmd.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd"));
            }
            sql += " ORDER BY Date, Id;";
            cmd.CommandText = sql;

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var rec = new Models.ResultRecord
                {
                    Id = rdr.GetString(0),
                    Date = DateTime.TryParse(rdr.GetString(1), out var dt) ? dt : DateTime.MinValue,
                    Club = rdr.GetString(2),
                    Venue = rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3),
                    PlayerId = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4),
                    PartnerId = rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    PlayerName = rdr.IsDBNull(6) ? string.Empty : rdr.GetString(6),
                    Partner = rdr.IsDBNull(7) ? string.Empty : rdr.GetString(7),
                    Hcp = rdr.IsDBNull(8) ? 0 : rdr.GetInt32(8),
                    Result = rdr.IsDBNull(9) ? 0 : rdr.GetInt32(9),
                    Position = rdr.IsDBNull(10) ? 0 : rdr.GetInt32(10)
                };
                list.Add(rec);
            }

            return list;
        }

        public async Task<string?> DeleteResultAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = "DELETE FROM Results WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<string?> ClearAllResultsAsync()
        {
            if (_conn is null) throw new InvalidOperationException("Database not initialized.");

            try
            {
                using var tran = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = "DELETE FROM Results;";
                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                return null;
            }
            catch (SqliteException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ============================================================================
        // NEW METHODS FOR NEW/EXISTING RESULTS WORKFLOW
        // ============================================================================

        /// <summary>
        /// Get all distinct venues that have results recorded.
        /// </summary>
        public async Task<List<string>> GetVenuesWithResultsAsync()
        {
            var list = new List<string>();
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT DISTINCT Venue 
FROM Results 
WHERE Venue IS NOT NULL AND Venue != '' 
ORDER BY Venue;";

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(rdr.GetString(0));
            }
            return list;
        }

        /// <summary>
        /// Get the single date for a specific venue (assumes one game per venue).
        /// Returns null if no results found for that venue.
        /// </summary>
        public async Task<DateTime?> GetDateForVenueAsync(string venue)
        {
            if (string.IsNullOrWhiteSpace(venue)) return null;
            if (_conn is null) return null;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT Date FROM Results WHERE Venue = $venue LIMIT 1;";
            cmd.Parameters.AddWithValue("$venue", venue);

            var scalar = await cmd.ExecuteScalarAsync();
            if (scalar == null || scalar == DBNull.Value) return null;

            return DateTime.TryParse(Convert.ToString(scalar), out var dt) ? dt : null;
        }

        /// <summary>
        /// Get distinct clubs that participated at a specific venue on a specific date.
        /// </summary>
        public async Task<List<string>> GetClubsForVenueDateAsync(string venue, DateTime date)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(venue)) return list;
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT DISTINCT ClubShortName 
FROM Results 
WHERE Venue = $venue AND Date = $date 
ORDER BY ClubShortName;";
            cmd.Parameters.AddWithValue("$venue", venue);
            cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(rdr.GetString(0));
            }
            return list;
        }

        /// <summary>
        /// Check if a result exists for a specific venue, date, and player.
        /// Returns the existing result ID if found, null otherwise.
        /// </summary>
        public async Task<(bool Exists, string? ExistingId)> CheckResultExistsAsync(string venue, DateTime date, string playerId)
        {
            if (string.IsNullOrWhiteSpace(venue) || string.IsNullOrWhiteSpace(playerId))
                return (false, null);
            if (_conn is null) return (false, null);

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id FROM Results 
WHERE Venue = $venue AND Date = $date AND PlayerId = $playerId 
LIMIT 1;";
            cmd.Parameters.AddWithValue("$venue", venue);
            cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$playerId", playerId);

            var scalar = await cmd.ExecuteScalarAsync();
            if (scalar == null || scalar == DBNull.Value) return (false, null);

            return (true, Convert.ToString(scalar));
        }

        /// <summary>
        /// Get results for a specific venue, date, and club.
        /// </summary>
        public async Task<List<Models.ResultRecord>> GetResultsByVenueDateClubAsync(string venue, DateTime date, string clubShort)
        {
            var list = new List<Models.ResultRecord>();
            if (string.IsNullOrWhiteSpace(venue) || string.IsNullOrWhiteSpace(clubShort)) return list;
            if (_conn is null) return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Date, ClubShortName, Venue, PlayerId, PartnerId, PlayerName, PartnerName, Hcp, Score, Position 
FROM Results 
WHERE Venue = $venue AND Date = $date AND ClubShortName = $club
ORDER BY Position, PlayerName;";

            cmd.Parameters.AddWithValue("$venue", venue);
            cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$club", clubShort);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new Models.ResultRecord
                {
                    Id = rdr.GetString(0),
                    Date = DateTime.TryParse(rdr.GetString(1), out var dt) ? dt : DateTime.MinValue,
                    Club = rdr.GetString(2),
                    Venue = rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3),
                    PlayerId = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4),
                    PartnerId = rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    PlayerName = rdr.IsDBNull(6) ? string.Empty : rdr.GetString(6),
                    Partner = rdr.IsDBNull(7) ? string.Empty : rdr.GetString(7),
                    Hcp = rdr.IsDBNull(8) ? 0 : rdr.GetInt32(8),
                    Result = rdr.IsDBNull(9) ? 0 : rdr.GetInt32(9),
                    Position = rdr.IsDBNull(10) ? 0 : rdr.GetInt32(10)
                });
            }
            return list;
        }

        // ============================================================================
        // DISPOSE
        // ============================================================================

        public void Dispose()
        {
            try { _conn?.Close(); } catch { /* ignore */ }
            _conn?.Dispose();
            _conn = null;
        }
    }
}