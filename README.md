# wxhook，微信hook
Windows 桌面程序 Hook 技术分析与开发示例。

微信hook版本4.1.11.52，仅支持此版本。wxhook4.1X

# WeixinHook
面向客服与运营场景的 **微信 PC 端消息助手**：实时收消息、一键发消息，把重复的点选操作交给工具完成。
---
## 能做什么
- **实时收消息**  
  新消息自动出现在面板里，不用反复翻聊天窗口。
- **一键发文本**  
  填好对方，输入内容，点发送即可；支持好友与群聊。
- **文件传输助手调试**  
  可先发到「文件传输助手」自测，确认链路正常再对客服务。
- **桌面小工具形态**  
  独立窗口操作，连接一次即可持续收发，适合日常挂机值守。
---
## 适合谁用
- 每天要回复大量重复咨询的客服  
- 需要固定话术、批量触达的运营同学  
- 想先把「收得到、发得出」跑通，再接自己业务逻辑的开发者  
---
## 使用体验（三步）
1. 启动微信 PC 客户端并登录  
2. 打开本工具，完成连接  
3. 在面板里查看收到的消息，或直接发送文本  
> 提示：首次使用建议先给「文件传输助手」发一条，确认收发都正常。
---
## 当前能力一览
| 能力 | 状态 |
|------|------|
| 接收文本消息 | ✅ |
| 发送文本消息 | ✅ |
| 好友 / 群聊对象 | ✅ |
| 图形界面操作 | ✅ |
图片、语音、表情等类型未开源
<img width="2118" height="1041" alt="image" src="https://github.com/user-attachments/assets/59469251-e58c-44dd-88b7-9d8454b46a8d" />

---
## 说明
本仓库仅供学习与个人效率工具参考，请遵守当地法律法规及平台使用规范，勿用于骚扰、刷量或任何违规用途。
---
## 反馈
有使用想法或功能需求，欢迎提 Issue。  
觉得有用的话给个 Star，方便后续更新迭代。
## 其他未开源接口一览，有需要的联系TG

| 方法 | 路径 | 摘要 |
|------|------|------|
| `POST` | `/api/send/text` | 文本发送 |
| `POST` | `/api/send/chatroom` | 群内发送 |
| `POST` | `/api/send_at_text` | 群内艾特人 |
| `GET` | `/api/msg/pop` | 获取实时消息，轮询 |
| `POST` | `/api/send/image` | 个人发送图片 |
| `POST` | `/api/send/chatroom/image` | 群内发送图片 |
| `POST` | `/api/send/video` | 个人发送视频 |
| `POST` | `/api/send/chatroom/video` | 群内发送视频 |
| `GET` | `/api/get_db_handles` | 获取数据库句柄，需要登陆前注入有效 |
| `POST` | `/api/exec_sql` | 数据库方式获取联系人 |
| `POST` | `/api/get_room_members` | 数据库方式获取群成员 |
| `POST` | `/api/get_room_members_hook` | 获取群成员列表 |
| `GET` | `/api/get_contacts` | 内存方式获取联系人列表 |
| `POST` | `/api/download_image` | cdn下载图片 |
| `POST` | `/api/get_room_info` | 群详情（ 群主/群名/头像/公告/成员数） |
| `GET` | `/api/get_self` | 从 DB 路径解析本机 wxid |
| `POST` | `/api/send_pat` | 拍一拍，走的网络层，本地UI不显示 |
| `POST` | `/api/send_card_msg` | 发送名片信息，走的网络层，本地UI不显示 |
| `POST` | `/api/send_xml` | 发送链接信息，走的网络层，本地UI不显示 |
| `POST` | `/api/send_emotion_msg` | 发送本地GIF信息，走的网络层，本地UI不显示 |
| `POST` | `/api/send_app_msg` | 发送卡片信息都可以发 包括但不限于 小程序 位置 音乐卡片等等，走的网络层，本地UI不显示 |
| `POST` | `/api/send_fav_emotion` | 发送收藏表情，走的网络层，本地UI不显示 |
| `POST` | `/api/cdn_video_forward` | cdn转发视频，走的网络层，本地UI不显示 |
| `POST` | `/api/send_cdn_img_msg` | cdn发送图片(无源可用做转发消息) |
| `POST` | `/api/send_mp3_voice` | 发送mp3语音，走的网络层，本地UI不显示 |
| `POST` | `/api/send_applet_msg` | 发送小程序，走的网络层，本地UI不显示 |
| `GET` | `/api/get_favs` | 获取收藏列表 |
| `POST` | `/api/set_room_announcement_pb` | 设置群公告 |
| `POST` | `/api/send_location_msg` | 发送位置消息，走的网络层，本地UI不显示 |
| `POST` | `/api/sns_post` | 发送朋友圈 |
| `POST` | `/api/get_profile_new` | 语音转文本 |
| `GET` | `/api/get_profile_cache` | 获取自身信息 |
| `POST` | `/api/net_scene_search_contact` | 搜索微信号/手机号 |
| `POST` | `/api/add_friend` | 添加好友 |
| `POST` | `/api/js_login` | 获取小程序code |
| `POST` | `/api/verify_friend` | 同意好友申请(有变动) |
| `POST` | `/api/sync_msg/clear` | 清掉旧乱码，同意加好友申请，用的测试接口 |
| `GET` | `/api/sync_msg/pop` | 专门接收同意好友申请的消息接口 |
| `POST` | `/api/get_a8key` | 群聊获取A8key |
| `POST` | `/api/creat_chat_room` | 创建群聊 |
| `POST` | `/api/invite_member_to_chat_room` | 邀请进入群聊 |
| `POST` | `/api/del_member_from_chat_room` | 踢出群成员 |
| `POST` | `/api/reflash_qrcode` | 获取登录二维码 |
<img width="2322" height="1224" alt="image" src="https://github.com/user-attachments/assets/25dbebde-1d49-4133-8f89-a1fd1f7c815c" />
<img width="2246" height="1233" alt="image" src="https://github.com/user-attachments/assets/8cbff035-3a60-4e59-8bf1-f192b1574389" />
<img width="2280" height="1332" alt="image" src="https://github.com/user-attachments/assets/e9464026-6f59-48dd-bda7-3486c60c09aa" />

我的TG，需要更多功能可以联系我。https://t.me/t2599em423
<img width="300" height="300" alt="image" src="https://github.com/user-attachments/assets/d8501e2b-2131-43da-8890-b16ae2990df5" />


