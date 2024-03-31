/*

Design Documentation:

// Kind of a in-memory DB
Singleton Instance broker Service: (Since All counts Need to be read by all the connections and need to cater respective counts)

1. Connections Visited/Read Count Map
2. All Counts List Posted By 1700+ micro services

API's:

1. GetCountIfAvaialable --> used by Get SSE (1 item which was already not visited/read by current connection)
2. ResetCount --> used by Get SSE (It will verify expiration time of Count Returned by GetCountIfAvaialable and removes it from All Count List)
3. SetCount --> used post end point (It will add an item to All Counts List)

Deployment Strategies:

Cons:
    - This Feature/Function Can't be deployed inside a Server-less architecture because as it is tied to in-memory data
    - If any disaster occurs we will lose 2 mins of counts refreshing data and effects the connected users.

Future Steps To Make it Ready for Serverless:
    - Write data to any Cache server/File System/Cloud Storage/SQL to a Temporary Storage
    - Get the Data From Storage only once when process starts
    - Keep updating the Data in Temporary Storage for every one minute as we keep removing or adding data to in-memory data

*/

public record Count(string Domain, double count, int PracticeId, int? UserId, DateTime createdOn, Guid uuid);

public record PostCount(string Domain, double count, int[] UserIds);


public class CountBrokerService
{
    /*
        maintaining the list of all the counts posted from 1700+ micro services
    */
    private List<Count> allCounts = new List<Count>();

    /*
        Here key is (connectionId|countUid) and value is visited/read this count or not bool
        ex: (connectionId|countUid): true
    */
    private Dictionary<string, bool> connectionsReadCounts = new Dictionary<string, bool>();

    /*
      Storing all the list counts in memory
    */
    public void SetCount(List<Count> _cnt)
    {
        allCounts.AddRange(_cnt);
    }

    /*
        Checking the TimeSpan and removing the Item from list if difference is >= 60 Secs
    */
    public void ResetCount(Count _cnt)
    {
        TimeSpan spanDifference = DateTime.Now.Subtract(_cnt.createdOn);

        if (spanDifference.Seconds >= 60)
            allCounts.Remove(_cnt);

        // memory cleaning up visited/read counts-connections as its not required to be present
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
    public List<Count> GetCountIfAvailaible(int PracticeId, Guid connectionId)
    {
        IEnumerable<Count> countsByPractice = allCounts.Where(item => item.PracticeId == PracticeId);
        List<Count> unVisitedCounts = new List<Count>();

        if (countsByPractice != null)
        {
            foreach (Count unreadCount in countsByPractice)
            {
                // generating a unique combination to keep track of read counts for current connection user so that we won't return it again back
                string combinationKey = unreadCount?.uuid.ToString() + "|" + connectionId.ToString();

                if (!connectionsReadCounts[combinationKey])
                {
                    connectionsReadCounts.Add(combinationKey, true);
                    unVisitedCounts.Add(unreadCount);
                }
            }
        }

        return unVisitedCounts;
    }
}