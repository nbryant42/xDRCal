namespace xDRCal.Tests
{
    [TestClass()]
    public class UtilTests
    {
        [TestMethod()]
        public void PQCodeToNitsTest()
        {
            Assert.AreEqual(0.0f, Util.PQCodeToNits(0));
            Assert.AreEqual(4.042245E-05f, Util.PQCodeToNits(1));
            Assert.AreEqual(0.00013111375f, Util.PQCodeToNits(2));
            Assert.AreEqual(0.00026237f, Util.PQCodeToNits(3));
            Assert.AreEqual(0.00043151382f, Util.PQCodeToNits(4));
            Assert.AreEqual(0.0006374664f, Util.PQCodeToNits(5));
            // This is where you get above a 1/3000 contrast ratio if peak white is 80 nits.
            // (80 / 3000 = 0.026666666)
            Assert.AreEqual(0.02769266f, Util.PQCodeToNits(36));
            // 400 / 3000 = 0.133333333
            Assert.AreEqual(0.13374512f, Util.PQCodeToNits(72));
            // 600 / 3000 = 0.2
            Assert.AreEqual(0.20153938f, Util.PQCodeToNits(85));
            Assert.AreEqual(79.97542f, Util.PQCodeToNits(497));
            Assert.AreEqual(80.76884f, Util.PQCodeToNits(498));
            Assert.AreEqual(91.79462f, Util.PQCodeToNits(511));
            Assert.AreEqual(99.259094f, Util.PQCodeToNits(519));
            Assert.AreEqual(100.230125f, Util.PQCodeToNits(520));
            Assert.AreEqual(199.15353f, Util.PQCodeToNits(592));
            Assert.AreEqual(201.02339f, Util.PQCodeToNits(593));
            Assert.AreEqual(981.1462f, Util.PQCodeToNits(767));
            Assert.AreEqual(9907.443f, Util.PQCodeToNits(1022));
            Assert.AreEqual(10000.0f, Util.PQCodeToNits(1023));
        }
    }
}