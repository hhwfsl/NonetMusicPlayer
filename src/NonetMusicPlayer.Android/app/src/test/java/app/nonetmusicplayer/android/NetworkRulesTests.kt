package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.network.*
import org.junit.Assert.*
import org.junit.Test

class NetworkRulesTests {
    @Test fun localNumericAddressesOnly() {
        assertTrue(PeerSecurity.privateAddress("192.168.10.2"));assertTrue(PeerSecurity.privateAddress("10.0.2.2"));assertTrue(PeerSecurity.privateAddress("172.31.4.5"))
        listOf("8.8.8.8","172.32.1.1","example.com","0.0.0.0").forEach {assertFalse(PeerSecurity.privateAddress(it))}
    }
    @Test fun singleByteRanges() {
        assertEquals(0L..9L,peerRange("bytes=0-",10));assertEquals(7L..9L,peerRange("bytes=-3",10));assertEquals(3L..4L,peerRange("bytes=3-4",10))
        listOf("bytes=10-","bytes=3-1","bytes=1-2,3-4","bytes=-0").forEach {assertNull(peerRange(it,10))}
    }
    @Test fun transientHeartbeatFailuresDoNotEndTheSession() {
        val health=PeerHeartbeatPolicy()
        assertEquals(5000L,health.delayMs)
        assertFalse(health.failed(1000));assertEquals(2000L,health.delayMs)
        assertFalse(health.failed(7000));health.succeeded()
        assertEquals(5000L,health.delayMs)
        assertFalse(health.failed(30000));assertFalse(health.failed(36000))
        assertTrue(health.failed(42000));assertTrue(health.failed(60000));assertFalse(health.reachable)
        health.succeeded();assertTrue(health.reachable)
    }
    @Test fun fastRepeatedFailuresStillAllowARecoveryWindow() {
        val health=PeerHeartbeatPolicy()
        assertFalse(health.failed(100));assertFalse(health.failed(2100));assertFalse(health.failed(4100))
        assertTrue(health.failed(12100));assertTrue(health.failed(30100));health.succeeded();assertFalse(health.failed(50000))
    }
    @Test fun authenticatedReverseActivityKeepsAnIdlePeerOnline() {
        val health=PeerHeartbeatPolicy();assertFalse(health.failed(1000))
        for(now in 5000L..125000L step 5000) {health.contact(now);assertFalse(health.failed(now+1000))}
        assertTrue(health.failed(150000));assertTrue(health.failed(155000));health.contact(155001);assertTrue(health.reachable)
    }
    @Test fun aDayWithoutNetworkRetainsRecoverableHealthAndBoundedBackoff() {
        val health=PeerHeartbeatPolicy()
        for(now in 0L..86_400_000L step 1000) {health.failed(now);assertTrue(health.delayMs in 2000..15000)}
        assertFalse(health.reachable);health.succeeded();assertTrue(health.reachable);assertEquals(5000L,health.delayMs)
    }
}
