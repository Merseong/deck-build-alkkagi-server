using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using UnityEngine;

public class DatabaseManager : SingletonBehaviour<DatabaseManager>
{
    private DatabaseReference dbRef;

    private void Awake()
    {
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;
    }

    /// <param name="uid"></param>
    /// <param name="callback">return null if user not exist</param>
    public async void FindUser(uint uid, Action<UserDataSchema> callback)
    {
        if (uid == 0)
        {
            callback(null);
            return;
        }

        var userSnapshot = await dbRef.Child("users").Child(uid.ToString()).GetValueAsync();
        if (userSnapshot.Exists)
        {
            var user = JsonUtility.FromJson<UserDataSchema>(userSnapshot.GetRawJsonValue());
            user.password = null;
            callback(user);
            return;
        }
        callback(null);
    }

    public async void TryLogin(string accountData, Action<UserDataSchema, bool> callback)
    {
        var loginData = accountData.Split('@');
        var userData = await FindByIdAsync(loginData[0]);
        if (userData != null && userData.password == loginData[1])
        {
            userData.password = "";
            callback(userData, true);
        }
        else
        {
            callback(new UserDataSchema { loginId = loginData[0] }, false);
        }
    }

    public UserDataPacket PackUserData(UserDataSchema data)
    {
        return new UserDataPacket
        {
            uid = data.uid,
            deckUnlock = data.deckUnlock,
            honorLose = data.honorLose,
            honorPoint = data.honorPoint,
            honorWin = data.honorWin,
            lose = data.lose,
            moneyPoint = data.moneyPoint,
            nickname = data.nickname,
            rating = data.rating,
            win = data.win,
        };
    }

    public async Task<UserDataSchema> FindByIdAsync(string id)
    {
        var snapshot = await dbRef.Child("users").OrderByChild("loginId").EqualTo(id).GetValueAsync();
        if (!snapshot.HasChildren) return null;
        foreach (var child in snapshot.Children)
        {
            return JsonUtility.FromJson<UserDataSchema>(child.GetRawJsonValue());
        }
        return null;
    }

    public async void RegisterUser(UserDataPacket newUser, string id, string password, Action<bool> callback)
    {
        if (id.Contains('@'))
        {
            callback(false);
            return;
        }
        if (await FindByIdAsync(id) != null)
        {
            callback(false);
            return;
        }

        var newData = new UserDataSchema(newUser)
        {
            loginId = id,
            password = password
        };
        uint uid = (uint)UnityEngine.Random.Range(0, int.MaxValue);
        var snapshot = await dbRef.Child("users").Child(uid.ToString()).GetValueAsync();
        while (snapshot.Exists)
        {
            uid = (uint)UnityEngine.Random.Range(0, int.MaxValue);
            snapshot = await dbRef.Child("users").Child(uid.ToString()).GetValueAsync();
        }
        newData.uid = uid;
        string json = JsonUtility.ToJson(newData);

        await dbRef.Child("users").Child(uid.ToString()).SetRawJsonValueAsync(json);
        callback(true);
    }

    public async void UpdateUser(uint uid, Dictionary<string, object> updateDict, Action<UserDataSchema> callback = null)
    {
        var userRef = dbRef.Child("users").Child(uid.ToString());
        await userRef.UpdateChildrenAsync(updateDict);
        if (callback != null)
        {
            var newDataSnapshot = await userRef.GetValueAsync();
            callback.Invoke(JsonUtility.FromJson<UserDataSchema>(newDataSnapshot.GetRawJsonValue()));
        }
        return;
    }

    public async void ReportProblem(uint reporterUid, string message)
    {
        var report = new ReportSchema
        {
            Datetime = DateTime.UtcNow.ToString(),
            ReporterUid = reporterUid,
            Content = message,
        };
        var json = JsonUtility.ToJson(report);
        await dbRef.Child("reports").Child($"{report.Datetime}-{reporterUid}").SetRawJsonValueAsync(json);
    }
}

public class UserDataSchema
{
    public uint uid;
    public string loginId;
    public string password;
    public string nickname;
    public uint honorWin;
    public uint honorLose;
    public uint win;
    public uint lose;
    public uint rating;
    public string deckUnlock;
    public uint moneyPoint;
    public uint honorPoint;

    public UserDataSchema() { }

    public UserDataSchema(UserDataPacket packet)
    {
        uid = packet.uid;
        nickname = packet.nickname;
        honorWin = packet.honorWin;
        honorLose = packet.honorLose;
        win = packet.win;
        lose = packet.lose;
        rating = packet.rating;
        deckUnlock = packet.deckUnlock;
        moneyPoint = packet.moneyPoint;
        honorPoint = packet.honorPoint;
    }

    public Dictionary<string, object> GetDict()
    {
        Dictionary<string, object> result = new Dictionary<string, object>();
        result["uid"] = uid;
        result["loginIn"] = loginId;
        result["nickname"] = nickname;
        result["honorWin"] = honorWin;
        result["honorLose"] = honorLose;
        result["win"] = win;
        result["lose"] = lose;
        result["rating"] = rating;
        result["deckUnlock"] = deckUnlock;
        result["moneyPoint"] = moneyPoint;
        result["honorPoint"] = honorPoint;

        return result;
    }
}

public class ReportSchema
{
    public string Datetime;
    public uint ReporterUid;
    public string Content;
}
