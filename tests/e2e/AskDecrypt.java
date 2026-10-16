/*
 * Reference implementation of the SEB Server's Cryptor.decryptASK
 * (https://github.com/SafeExamBrowser/seb-server):
 *
 *   key = PBKDF2WithHmacSHA1(connectionToken, salt, 1024 iterations, 256 bits)
 *   data = AES/GCM/NoPadding with the first 16 bytes of the cipher text as IV
 *
 * Used by scripts/e2e.sh to verify the app signature key sent by seb-linux during the
 * mock handshake against the exact same JCE code path as the SEB Server.
 *
 * Usage:
 *   javac AskDecrypt.java
 *   java AskDecrypt decrypt <token> <salt-hex> <cipher-hex>
 *   java AskDecrypt encrypt <token> <salt-hex> <plaintext> <iv-hex>
 */

import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import javax.crypto.spec.PBEKeySpec;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.HexFormat;

public class AskDecrypt {
    public static void main(String[] args) throws Exception {
        var hex = HexFormat.of();
        String mode = args[0];
        String token = args[1];
        byte[] salt = hex.parseHex(args[2]);
        PBEKeySpec keySpec = new PBEKeySpec(token.toCharArray(), salt, 1024, 256);
        byte[] key = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA1").generateSecret(keySpec).getEncoded();

        if (mode.equals("decrypt")) {
            byte[] all = hex.parseHex(args[3]);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"),
                new GCMParameterSpec(128, Arrays.copyOfRange(all, 0, 16)));
            byte[] plain = cipher.doFinal(Arrays.copyOfRange(all, 16, all.length));
            System.out.println(new String(plain, StandardCharsets.UTF_8));
        } else {
            byte[] iv = hex.parseHex(args[4]);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(key, "AES"),
                new GCMParameterSpec(128, iv));
            byte[] ct = cipher.doFinal(args[3].getBytes(StandardCharsets.UTF_8));
            byte[] out = new byte[iv.length + ct.length];
            System.arraycopy(iv, 0, out, 0, iv.length);
            System.arraycopy(ct, 0, out, iv.length, ct.length);
            System.out.println(hex.formatHex(out));
        }
    }
}
