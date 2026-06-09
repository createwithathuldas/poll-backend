namespace PollApi.Models
{
    public class FifaTeams
    {
        public int Id { get; set; }
        public string TeamName { get; set; }
        public string Confederation { get; set; }
        public int FIFA_Ranking { get; set; }
        public string Captain { get; set; }
        public List<string> Players { get; set; }
        public string TopScorer { get; set; }
        public string Coach { get; set; }
        public int WorldCupWins { get; set; }
        public int WorldCupAppearances { get; set; }
        public bool IsDeleted { get; set; }
 
        public ICollection<TeamGoalkeeper> Goalkeepers { get; set; }
        public ICollection<Vote> Votes { get; set; }
    }
}