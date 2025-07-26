namespace xDRCal.Tests
{
    [TestClass()]
    public class UtilTests
    {
        [TestMethod()]
        public void PQCodeToNitsTest()
        {
            Assert.AreEqual(0.0f, EOTF.pq.ToNits(0));
            Assert.AreEqual(4.042245E-05f, EOTF.pq.ToNits(1));
            Assert.AreEqual(0.00013111375f, EOTF.pq.ToNits(2));
            Assert.AreEqual(0.00026237f, EOTF.pq.ToNits(3));
            Assert.AreEqual(0.00043151382f, EOTF.pq.ToNits(4));
            Assert.AreEqual(0.0006374664f, EOTF.pq.ToNits(5));
            // This is where you get above a 1/3000 contrast ratio if peak white is 80 nits.
            // (80 / 3000 = 0.026666666)
            Assert.AreEqual(0.02769266f, EOTF.pq.ToNits(36));
            // 400 / 3000 = 0.133333333
            Assert.AreEqual(0.13374512f, EOTF.pq.ToNits(72));
            // 600 / 3000 = 0.2
            Assert.AreEqual(0.20153938f, EOTF.pq.ToNits(85));
            Assert.AreEqual(79.97542f, EOTF.pq.ToNits(497));
            Assert.AreEqual(80.76884f, EOTF.pq.ToNits(498));
            Assert.AreEqual(91.79462f, EOTF.pq.ToNits(511));
            Assert.AreEqual(99.259094f, EOTF.pq.ToNits(519));
            Assert.AreEqual(100.230125f, EOTF.pq.ToNits(520));
            Assert.AreEqual(199.15353f, EOTF.pq.ToNits(592));
            Assert.AreEqual(201.02339f, EOTF.pq.ToNits(593));
            Assert.AreEqual(981.1462f, EOTF.pq.ToNits(767));
            Assert.AreEqual(9907.443f, EOTF.pq.ToNits(1022));
            Assert.AreEqual(10000.0f, EOTF.pq.ToNits(1023));
        }
    }
}