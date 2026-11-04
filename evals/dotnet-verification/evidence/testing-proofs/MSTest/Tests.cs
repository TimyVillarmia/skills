using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class FixtureProof {
 [TestMethod] public void FixtureInitializationRan() => Assert.IsFalse(string.IsNullOrEmpty(DatabaseFixture.ConnectionString));
}
