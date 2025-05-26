﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniInject;
using UniRx;
using UnityEngine;

public class WebCamManager : AbstractSingletonBehaviour, INeedInjection
{
    public static WebCamManager Instance => DontDestroyOnLoadManager.FindComponentOrThrow<WebCamManager>();

    [Inject]
    private Settings settings;

    [Inject]
    private SceneNavigator sceneNavigator;

    private WebCamTexture webCamTexture;
    private WebCamDevice webCamTextureDevice;

    protected override object GetInstance()
    {
        return Instance;
    }

    protected override void StartSingleton()
    {
        sceneNavigator.BeforeSceneChangeEventStream.Subscribe(_ => StopWebCam());
    }

    public List<WebCamDevice> GetWebCamDevices()
    {
        return WebCamTexture.devices.ToList();
    }

    public WebCamTexture StartSelectedWebCam()
    {
        WebCamDevice selectedWebCamDevice = GetSelectedWebCamDevice();
        if (selectedWebCamDevice.name == webCamTextureDevice.name
            && webCamTexture != null
            && webCamTexture.isPlaying)
        {
            // Re-use existing webCamTexture.
            return webCamTexture;
        }

        StopWebCam();
        Debug.Log($"Starting webcam '{selectedWebCamDevice.name}'");
        webCamTexture = new WebCamTexture(selectedWebCamDevice.name);
        webCamTexture.Play();
        webCamTextureDevice = selectedWebCamDevice;
        return webCamTexture;
    }

    public WebCamDevice GetSelectedWebCamDevice()
    {
        return GetWebCamDevices().FirstOrDefault(device => device.name == settings.CurrentWebcamDeviceName);
    }

    private void StopWebCam()
    {
        if (webCamTexture != null
            && webCamTexture.isPlaying)
        {
            Debug.Log($"Stopping webcam '{webCamTexture.name}'");
            webCamTexture.Stop();
        }
    }

    public void SaveSnapshot(string filePath)
    {
        if (webCamTexture == null
            || !webCamTexture.isPlaying)
        {
            return;
        }

        try
        {
            Texture2D tempTexture2D = new Texture2D(webCamTexture.width, webCamTexture.height);
            tempTexture2D.SetPixels(webCamTexture.GetPixels());

            byte[] bytes = tempTexture2D.EncodeToPNG();
            string directoryPath = Path.GetDirectoryName(filePath);
            if(!directoryPath.IsNullOrEmpty()
               && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            File.WriteAllBytes(filePath, bytes);

            GameObject.Destroy(tempTexture2D);
        }
        catch (Exception e)
        {
            throw new WebCamException($"Failed to save texture to '{filePath}'", e);
        }
    }
}
