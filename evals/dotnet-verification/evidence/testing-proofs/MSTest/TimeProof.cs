using Microsoft.VisualStudio.TestTools.UnitTesting;
// In production service:
public class TrialService(TimeProvider timeProvider)
{
    public bool IsExpired(DateTimeOffset startedAt) =>
        timeProvider.GetUtcNow() - startedAt > TimeSpan.FromDays(14);
}


[TestClass]
public class TimeProof {

[TestMethod]
public void Trial_After15Days_IsExpired()
{
    var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
    var service = new TrialService(fakeTime);
    var start = fakeTime.GetUtcNow();

    fakeTime.Advance(TimeSpan.FromDays(15));

    Assert.IsTrue(service.IsExpired(start));
}

}
