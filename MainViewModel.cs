
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GolfApp1.Data;
using GolfApp1.Models;

namespace GolfApp1.ViewModels
{
    internal sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly Database _db;

        public ObservableCollection<Club> Clubs { get; } = new();
        public ObservableCollection<Player> Players { get; } = new();

        internal MainViewModel(Database db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task LoadClubsAsync()
        {
            Clubs.Clear();
            var list = await _db.GetAllClubsAsync();
            foreach (var c in list) Clubs.Add(c);
            RaisePropertyChanged(nameof(Clubs));
        }

        public async Task LoadPlayersAsync(string clubShort)
        {
            Players.Clear();
            var list = await _db.GetPlayersByClubAsync(clubShort);
            foreach (var p in list) Players.Add(p);
            RaisePropertyChanged(nameof(Players));
        }

        public async Task UpsertClubAsync(Club club)
        {
            if (club is null) throw new ArgumentNullException(nameof(club));
            await _db.UpsertClubAsync(club);
        }

        public async Task<string?> UpsertPlayerAsync(Player player)
        {
            if (player is null) throw new ArgumentNullException(nameof(player));
            return await _db.UpsertPlayerAsync(player);
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion
    }
}