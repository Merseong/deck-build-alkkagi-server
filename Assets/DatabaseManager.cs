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

    private void Start()
    {
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;
    }

    public void FindUser(string sql, Action<UserDataSchema> callback)
    {
        callback(new UserDataSchema());
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
            callback(new UserDataSchema(), false);
        }
    }

    public UserDataPacket PackUserData(UserDataSchema data)
    {
        return new UserDataPacket();
    }

    public async Task<UserDataSchema> FindByIdAsync(string id)
    {
        var snapshot = await dbRef.Child("users").OrderByChild("loginId").EqualTo(id).LimitToFirst(1).GetValueAsync();
        if (snapshot.ChildrenCount == 0) return null;
        foreach (var child in snapshot.Children)
        {
            return JsonUtility.FromJson<UserDataSchema>(child.GetRawJsonValue());
        }
        return null;
    }

    public async void RegisterUser(UserDataPacket newUser, string id, string password, Action<bool> callback)
    {
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
    public uint deckUnlock;
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
}
