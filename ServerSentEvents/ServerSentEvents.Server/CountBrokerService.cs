public record Count(string Domain, double count, int PracticeId, int UserId, DateTime createdOn, Guid uuid);

public class CountBrokerService
{
    /*
        maintaining the list of all the counts posted from 1700+ micro services
    */
    private List<Count> cnts;

    /*
        Here key is (connectionId|countUid) and value is visited/read this count or not bool
        ex: (connectionId|countUid): true
    */
    private Dictionary<string, bool> connectionsReadCounts = new Dictionary<string, bool>();

    /*
      Storing all the list counts in memory
    */
    public void SetCount(Count _cnt)
    {
        cnts.Add(_cnt);
    }

    /*
        Checking the TimeSpan and removing the Item from list if difference is >= 60 Secs
    */
    public void ResetCount(Count _cnt)
    {
        TimeSpan spanDifference = DateTime.Now.Subtract(_cnt.createdOn);
        if (spanDifference.Seconds >= 60)
            cnts.Remove(_cnt);

        // memory cleaning up visited connection tree as its not required to be present
        var keysToRetire = connectionsReadCounts.Where(pv => pv.Key.StartsWith(_cnt.uuid.ToString()))
                                                .Select(item => item.Key);
        foreach (var key in keysToRetire)
        {
            connectionsReadCounts.Remove(key);
        }
    }

    /*
        Returning the Count available for current user and Practice if not already visited by the current connection
    */
    public Count? GetCountIfAvailaible(int PracticeId, int UserId, Guid connectionId)
    {
        var returned = cnts.FirstOrDefault(item => item.PracticeId == PracticeId && item.UserId == UserId);

        string combinationKey = returned?.uuid.ToString() + "|" + connectionId.ToString();

        if (!connectionsReadCounts[combinationKey])
        {
            connectionsReadCounts.Add(combinationKey, true);

            return returned;
        }
        return null;
    }
}