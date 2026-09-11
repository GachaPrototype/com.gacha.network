using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System.Security.Cryptography;
using System;

public class TestServer : MonoBehaviour
{
    const string HMAC_SECRET =
        "123456";

    IEnumerator Start()
    {
        // LOGIN

        string json =
@"{
""deviceId"":""test"",
""deviceModel"":""UnityEditor"",
""platform"":""Windows""
}";

        UnityWebRequest req =
            new UnityWebRequest(
                "http://localhost:3000/api/auth/login",
                "POST");

        req.uploadHandler =
            new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(json));

        req.downloadHandler =
            new DownloadHandlerBuffer();

        req.SetRequestHeader(
            "Content-Type",
            "application/json");

        yield return req.SendWebRequest();

        LoginResponse response =
            JsonUtility
            .FromJson<LoginResponse>(
                req.downloadHandler.text);

        Debug.Log("TOKEN");
        Debug.Log(response.token);


        // PROFILE

        req =
            UnityWebRequest.Get(
                "http://localhost:3000/api/player/profile");

        req.downloadHandler =
            new DownloadHandlerBuffer();

        req.SetRequestHeader(
            "Authorization",
            "Bearer " +
            response.token);

        yield return req.SendWebRequest();

        Debug.Log("PROFILE");

        Debug.Log(req.result);

        Debug.Log(
            req.downloadHandler.text);

        //ROSTER
        req = UnityWebRequest.Get("http://localhost:3000/api/player/roster");

        req.downloadHandler = new DownloadHandlerBuffer();

        req.SetRequestHeader(
        "Authorization",
        "Bearer " + response.token);

        yield return req.SendWebRequest();

        Debug.Log("ROSTER");
        Debug.Log(req.responseCode);
        Debug.Log(req.result);
        Debug.Log(req.downloadHandler.text);
        // Active Banner
        req = UnityWebRequest.Get(
    "http://localhost:3000/api/banner/active");

req.downloadHandler =
    new DownloadHandlerBuffer();

yield return req.SendWebRequest();

Debug.Log("ACTIVE BANNER");
Debug.Log(req.responseCode);
Debug.Log(req.result);
Debug.Log(req.downloadHandler.text);


// EQUIPMENT LIST

req = UnityWebRequest.Get(
    "http://localhost:3000/api/equipment/list");

req.downloadHandler =
    new DownloadHandlerBuffer();

req.SetRequestHeader(
    "Authorization",
    "Bearer " + response.token);

yield return req.SendWebRequest();

Debug.Log("EQUIPMENT LIST");
Debug.Log(req.responseCode);
Debug.Log(req.result);
Debug.Log(req.downloadHandler.text);
Debug.Log(req.error);

// CHARACTER TEMPLATE
string characterTemplateUrl = "http://localhost:3000/api/character/template?baseId=char_001";
Debug.Log("URL THỰC TẾ ĐANG GỬI: " + characterTemplateUrl);

// CHỈ GIỮ LẠI dòng này để sử dụng URL có tham số
req = UnityWebRequest.Get(characterTemplateUrl); 

// XÓA HOẶC KHÓA DÒNG BỊ THỪA NÀY LẠI:
// req = UnityWebRequest.Get("http://localhost:3000/api/character/template");

req.downloadHandler = new DownloadHandlerBuffer();

yield return req.SendWebRequest();

Debug.Log("CHARACTER TEMPLATE STATUS:");
Debug.Log(req.responseCode);
Debug.Log(req.result);
Debug.Log(req.downloadHandler.text);
Debug.Log(req.error);

        // SUMMON

        string summonBody =
@"{
""bannerId"":""banner_001"",
""count"":1
}";

        byte[] bodyBytes =
            Encoding.UTF8
            .GetBytes(
                summonBody);

        string timestamp =
            DateTimeOffset
            .UtcNow
            .ToUnixTimeSeconds()
            .ToString();

        string nonce =
            Guid
            .NewGuid()
            .ToString("N");

        // QUAN TRỌNG
        string build =
            "development";

        string bodyHash =
            Sha256Bytes(
                bodyBytes);

        string payload =
            $"POST:/api/gacha/summon:{timestamp}:{nonce}:{bodyHash}:{build}";

        string signature =
            HmacHex(
                HMAC_SECRET,
                payload);

        Debug.Log("PAYLOAD");
        Debug.Log(payload);

        Debug.Log("SIGNATURE");
        Debug.Log(signature);

        req =
            new UnityWebRequest(
                "http://localhost:3000/api/gacha/summon",
                "POST");

        req.uploadHandler =
            new UploadHandlerRaw(
                bodyBytes);

        req.downloadHandler =
            new DownloadHandlerBuffer();

        req.SetRequestHeader(
            "Content-Type",
            "application/json");

        req.SetRequestHeader(
            "Authorization",
            "Bearer " +
            response.token);

        req.SetRequestHeader(
            "X-Timestamp",
            timestamp);

        req.SetRequestHeader(
            "X-Nonce",
            nonce);

        req.SetRequestHeader(
            "X-BodyHash",
            bodyHash);

        req.SetRequestHeader(
            "X-Client-Build",
            build);

        req.SetRequestHeader(
            "X-Signature",
            signature);

        yield return req.SendWebRequest();

        Debug.Log("SUMMON");

        Debug.Log(
            req.responseCode);

        Debug.Log(
            req.result);

        Debug.Log(
            req.downloadHandler.text);
    }


    static string Sha256Bytes(
        byte[] data)
    {
        using SHA256 sha =
            SHA256.Create();

        byte[] hash =
            sha.ComputeHash(
                data);

        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }


    static string HmacHex(
        string secret,
        string text)
    {
        using HMACSHA256 h =
            new HMACSHA256(
                Encoding.UTF8
                .GetBytes(secret));

        byte[] hash =
            h.ComputeHash(
                Encoding.UTF8
                .GetBytes(text));

        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
