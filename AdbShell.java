package ai.berpiztu.keyplus;

import android.content.Context;
import android.os.Build;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyFactory;
import java.security.PrivateKey;
import java.security.cert.Certificate;
import java.security.cert.CertificateFactory;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

import io.github.muntashirakon.adb.AbsAdbConnectionManager;
import io.github.muntashirakon.adb.AdbStream;
import io.github.muntashirakon.adb.android.AdbMdns;

/**
 * The device's own adb daemon, reached over Wireless debugging as an ADB app
 * on the device reaches it (libadb-android): paired with the code Settings
 * shows, then asked to run a few shell commands as the shell user, who may
 * grant what no app grants itself. The key is the caller's, made for this one
 * time and never kept. Called from C# (AdbGrant.cs) through JNI, so it takes
 * and returns Java's own types only.
 */
public final class AdbShell {
    /** How long the daemon has to answer once paired. */
    private static final long ConnectTimeoutMillis = 10_000;

    private AdbShell() {
    }

    /**
     * The port Settings' "Pair device with pairing code" listens on while
     * its dialog is open, announced on the network (mDNS), or 0 when none was
     * seen in the time given.
     */
    public static int findPairingPort(Context context, long timeoutMillis) throws InterruptedException {
        AtomicInteger found = new AtomicInteger();
        CountDownLatch seen = new CountDownLatch(1);
        AdbMdns mdns = new AdbMdns(context, AdbMdns.SERVICE_TYPE_TLS_PAIRING, (host, port) -> {
            if (port > 0) {
                found.set(port);
                seen.countDown();
            }
        });
        mdns.start();
        try {
            seen.await(timeoutMillis, TimeUnit.MILLISECONDS);
            return found.get();
        } finally {
            mdns.stop();
        }
    }

    /**
     * Pairs with the pairing port by its code, connects to the daemon and
     * runs each command in turn: their output, or an exception that says what
     * failed. The key is PKCS #8, the certificate DER, both the caller's.
     */
    public static String run(Context context, int pairingPort, String code, byte[] privateKey, byte[] certificate,
                             String[] commands) throws Exception {
        PrivateKey key = KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(privateKey));
        Certificate certified = CertificateFactory.getInstance("X.509")
                .generateCertificate(new ByteArrayInputStream(certificate));
        try (Daemon daemon = new Daemon(key, certified)) {
            daemon.pair(pairingPort, code);
            if (!daemon.connectTls(context, ConnectTimeoutMillis)) {
                throw new IOException("Wireless debugging did not answer: check it is still on.");
            }

            StringBuilder output = new StringBuilder();
            for (String command : commands) {
                try (AdbStream stream = daemon.openStream("shell:" + command)) {
                    output.append(readAll(stream.openInputStream()));
                }
            }

            return output.toString();
        }
    }

    /** A shell command's output, read until the daemon closes its stream as the command ends. */
    private static String readAll(InputStream input) throws IOException {
        ByteArrayOutputStream read = new ByteArrayOutputStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = input.read(buffer)) != -1) {
            read.write(buffer, 0, count);
        }

        return read.toString(StandardCharsets.UTF_8.name());
    }

    /** libadb's connection, with the key and certificate it is given. */
    private static final class Daemon extends AbsAdbConnectionManager {
        private final PrivateKey key;
        private final Certificate certificate;

        Daemon(PrivateKey key, Certificate certificate) {
            this.key = key;
            this.certificate = certificate;
            setApi(Build.VERSION.SDK_INT);
        }

        @Override
        protected PrivateKey getPrivateKey() {
            return key;
        }

        @Override
        protected Certificate getCertificate() {
            return certificate;
        }

        @Override
        protected String getDeviceName() {
            return "KeyPlus";
        }
    }
}
