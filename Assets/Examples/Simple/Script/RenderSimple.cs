﻿using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using System;
using System.Runtime.InteropServices;

public class RenderSimple : MonoBehaviour {

    //摄像头参数(INPUT)
    public string currentDeviceName;
    public int cameraWidth=1280;
    public int cameraHeight=720;
    public int cameraFrameRate=30;

    WebCamTexture wtex; //Unity的外部相机类
    //byte[] img_bytes;
    Color32[] webtexdata;   //用于保存每帧从相机类获取的数据
    GCHandle img_handle;    //webtexdata的GCHandle
    IntPtr p_img_ptr;   //webtexdata的指针

    byte[] img_nv21;    //NV21格式的buffer的数组
    GCHandle img_nv21_handle;   //img_nv21的GCHandle
    IntPtr p_img_nv21_ptr;  //img_nv21的指针

    //SDK返回(OUTPUT)
    int m_fu_texid = 0; //SDK返回的纹理ID
    Texture2D m_rendered_tex;   //用SDK返回的纹理ID新建的纹理

    //标记参数
    bool m_tex_created; //m_rendered_tex是否已被创建，这个不需要每帧创建，纹理ID不变就不要重新创建

    //渲染显示UI
    public RawImage RawImg_BackGroud;   //用来显示相机结果的UI控件
    public Texture2D InputTex;  //通过纹理来往SDK内部传输数据

    const int SLOTLENGTH = 1;
    int[] itemid_tosdk;
    GCHandle itemid_handle;
    IntPtr p_itemsid;

    //切换相机
    public void SwitchCamera()
    {
        foreach (WebCamDevice device in WebCamTexture.devices)
        {
            if (currentDeviceName != device.name)
            {
                if (wtex != null && wtex.isPlaying) wtex.Stop();
                currentDeviceName = device.name;
                wtex = new WebCamTexture(currentDeviceName, cameraWidth, cameraHeight, cameraFrameRate);
                wtex.Play();
                FaceunityWorker.FixRotation(!device.isFrontFacing);
                break;
            }
        }
    }



    // 初始化摄像头 
    public IEnumerator InitCamera()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null)
            {
                Debug.Log("No Camera detected!");
            }
            else
            {
                currentDeviceName = devices[0].name;
                wtex = new WebCamTexture(currentDeviceName, cameraWidth, cameraHeight, cameraFrameRate);
                wtex.Play();
                FaceunityWorker.FixRotation(!devices[0].isFrontFacing);
            }
        }


        if (img_handle.IsAllocated)
            img_handle.Free();
        webtexdata = new Color32[wtex.width * wtex.height];
        img_handle = GCHandle.Alloc(webtexdata, GCHandleType.Pinned);
        p_img_ptr = img_handle.AddrOfPinnedObject();

        img_nv21 = new byte[wtex.width * wtex.height * 3 / 2];
        img_nv21_handle = GCHandle.Alloc(img_nv21, GCHandleType.Pinned);
        p_img_nv21_ptr = img_nv21_handle.AddrOfPinnedObject();
    }

    //当SDK初始化完毕后执行事件，即初始化相机
    void Awake()
    {
        FaceunityWorker.OnInitOK += InitApplication;
        if (itemid_tosdk == null)
        {
            itemid_tosdk = new int[SLOTLENGTH];
            itemid_handle = GCHandle.Alloc(itemid_tosdk, GCHandleType.Pinned);
            p_itemsid = itemid_handle.AddrOfPinnedObject();
        }
    }

    //初始化相机
    void InitApplication(object source, EventArgs e)
    {
        StartCoroutine(InitCamera());
        StartCoroutine(LoadItem(Util.GetStreamingAssetsPath() + "/faceunity/EmptyItem.bytes"));
    }

    //四种数据输入格式，详见文档
    public enum UpdateDataMode
    {
        NV21,
        Dual,
        Image,
        ImageTexId
    }

    public UpdateDataMode updateDataMode = UpdateDataMode.Image;

    void Update()
    {
        if (InputTex != null)
        {
            UpdateData(IntPtr.Zero, (int)InputTex.GetNativeTexturePtr(), InputTex.width, InputTex.height, UpdateDataMode.ImageTexId);
            return;
        }
		
        if (wtex != null && wtex.isPlaying)
        {
            if (wtex.didUpdateThisFrame)
            {
                if (updateDataMode == UpdateDataMode.ImageTexId)
                {
                    UpdateData(IntPtr.Zero, (int)wtex.GetNativeTexturePtr(), wtex.width, wtex.height, updateDataMode);
                }
                else
                {
                    if (webtexdata.Length != wtex.width * wtex.height)
                    {
                        if (img_handle.IsAllocated)
                            img_handle.Free();
                        webtexdata = new Color32[wtex.width * wtex.height];
                        img_handle = GCHandle.Alloc(webtexdata, GCHandleType.Pinned);
                        p_img_ptr = img_handle.AddrOfPinnedObject();
                    }
                    wtex.GetPixels32(webtexdata);

                    if (updateDataMode == UpdateDataMode.Image)
                    {
                        UpdateData(p_img_ptr, 0, wtex.width, wtex.height, updateDataMode);
                    }
                    else if (updateDataMode == UpdateDataMode.NV21 || updateDataMode == UpdateDataMode.Dual)
                    {
                        int[] argb = new int[wtex.width * wtex.height];       //模拟NV21模式,仅测试用,仅安卓手机上能正常运行
                        for (int i = 0; i < webtexdata.Length; i++)
                        {
                            argb[i] = 0;
                            argb[i] |= (webtexdata[i].a << 24);
                            argb[i] |= (webtexdata[i].r << 16);
                            argb[i] |= (webtexdata[i].g << 8);
                            argb[i] |= (webtexdata[i].b);
                        }
                        encodeYUV420SP(img_nv21, argb, wtex.width, wtex.height);
                        if (updateDataMode == UpdateDataMode.NV21)
                            UpdateData(p_img_nv21_ptr, 0, wtex.width, wtex.height, updateDataMode);
                        else
                            UpdateData(p_img_nv21_ptr, (int)wtex.GetNativeTexturePtr(), wtex.width, wtex.height, updateDataMode);
                    }
                }
            }
        }
    }

    private void OnApplicationQuit()
    {
        if (img_handle != null && img_handle.IsAllocated)
        {
            img_handle.Free();
        }
        if (img_nv21_handle != null && img_nv21_handle.IsAllocated)
        {
            img_nv21_handle.Free();
        }
        UnLoadItem();
    }


    void OnApplicationPause(bool isPause)
    {
        if (isPause)
        {
            Debug.Log("Pause");
            m_tex_created = false;
        }
        else
        {
            Debug.Log("Start");
        }
    }






    /**\brief 往SDK输入数据并根据返回的纹理ID新建一个纹理，绑定在UI上，这个返回不是即时的，首次输入数据后真正执行是在GL.IssuePluginEvent执行的时候，因此纹理ID会在下一帧返回\param ptr 输入数据buffer的指针\param texid 输入数据的纹理ID\param w 该帧图片的宽\param h 该帧图片的高\param mode 四种数据输入格式，详见文档\return 无    */
    public void UpdateData(IntPtr ptr,int texid,int w,int h, UpdateDataMode mode)
    {
        if (mode == UpdateDataMode.NV21)
            FaceunityWorker.SetNV21Input(ptr, 0, w, h);
        else if (mode == UpdateDataMode.Dual)
            FaceunityWorker.SetDualInput(ptr, texid, 0, w, h);
        else if (mode == UpdateDataMode.Image)
            FaceunityWorker.SetImage(ptr, 0, false, w, h);
        else if (mode == UpdateDataMode.ImageTexId)
            FaceunityWorker.SetImageTexId(texid, 0, w, h);
        if (m_tex_created == false)
        {
            m_fu_texid = FaceunityWorker.fu_GetNamaTextureId();
            if (m_fu_texid > 0)
            {
                m_rendered_tex = Texture2D.CreateExternalTexture(w, h, TextureFormat.RGBA32, false, true, (IntPtr)m_fu_texid);
                Debug.LogFormat("Texture2D.CreateExternalTexture:{0}\n", m_fu_texid);
                if (RawImg_BackGroud != null)
                {
                    RawImg_BackGroud.texture = m_rendered_tex;
                    RawImg_BackGroud.gameObject.SetActive(true);
                    Debug.Log("m_rendered_tex: " + m_rendered_tex.GetNativeTexturePtr());
                }
                m_tex_created = true;
            }
        }
    }

    // untested function，将ARGB转化成NV21，仅用来测试
    // byte[] yuv = new byte[inputWidth * inputHeight * 3 / 2];
    //    encodeYUV420SP(yuv, argb, inputWidth, inputHeight);
    void encodeYUV420SP(byte[] yuv420sp, int[] argb, int width, int height)
    {
        int frameSize = width * height;

        int yIndex = 0;
        int uvIndex = frameSize;

        int a, R, G, B, Y, U, V;
        int index = 0;
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {

                a = (int)(argb[index] & 0xff000000) >> 24; // a is not used obviously
                R = (argb[index] & 0xff0000) >> 16;
                G = (argb[index] & 0xff00) >> 8;
                B = (argb[index] & 0xff) >> 0;

                // well known RGB to YUV algorithm
                Y = ((66 * R + 129 * G + 25 * B + 128) >> 8) + 16;
                U = ((-38 * R - 74 * G + 112 * B + 128) >> 8) + 128;
                V = ((112 * R - 94 * G - 18 * B + 128) >> 8) + 128;

                // NV21 has a plane of Y and interleaved planes of VU each sampled by a factor of 2
                //    meaning for every 4 Y pixels there are 1 V and 1 U.  Note the sampling is every other
                //    pixel AND every other scanline.
                yuv420sp[yIndex++] = (byte)((Y < 0) ? 0 : ((Y > 255) ? 255 : Y));
                if (j % 2 == 0 && index % 2 == 0)
                {
                    yuv420sp[uvIndex++] = (byte)((V < 0) ? 0 : ((V > 255) ? 255 : V));
                    yuv420sp[uvIndex++] = (byte)((U < 0) ? 0 : ((U > 255) ? 255 : U));
                }

                index++;
            }
        }
    }

    public IEnumerator LoadItem(string path, int slotid = 0)
    {
        Debug.Log("LoadItem:" + path);
        WWW bundledata = new WWW(path);
        yield return bundledata;
        byte[] bundle_bytes = bundledata.bytes;
        GCHandle hObject = GCHandle.Alloc(bundle_bytes, GCHandleType.Pinned);
        IntPtr pObject = hObject.AddrOfPinnedObject();

        int itemid = FaceunityWorker.fu_CreateItemFromPackage(pObject, bundle_bytes.Length);
        hObject.Free();

        if (itemid_tosdk[slotid] > 0)
            UnLoadItem(slotid);

        itemid_tosdk[slotid] = itemid;

        FaceunityWorker.fu_setItemIds(p_itemsid, SLOTLENGTH, IntPtr.Zero);

        Debug.Log("LoadItem Finish");
    }

    public bool UnLoadItem(int slotid = 0)
    {
        if (slotid >= 0 && slotid < SLOTLENGTH)
        {
            FaceunityWorker.fu_DestroyItem(itemid_tosdk[slotid]);
            itemid_tosdk[slotid] = 0;
            Debug.LogFormat("UnLoadItem slotid = {0}", slotid);
            return true;
        }
        Debug.LogError("UnLoadItem Faild!!!");
        return false;
    }
}
