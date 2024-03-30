public record Count(string Domain, double count, int PracticeId, int UserId);

public class CountBrokerService
{
    private Count? cnt;

    public void SetCount(Count _cnt)
    {
        cnt = _cnt;
    }

    public void ResetCount()
    {
        cnt = null;
    }

    public Count? GetCountIfAvailaible()
    {
        return cnt;
    }
}