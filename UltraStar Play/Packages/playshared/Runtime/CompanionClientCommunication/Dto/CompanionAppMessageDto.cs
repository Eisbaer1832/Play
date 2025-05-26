﻿public class CompanionAppMessageDto : JsonSerializable
{
    public CompanionAppMessageType MessageType { get; set; }

    public long UnixTimeMilliseconds { get; set; }  

    public CompanionAppMessageDto()
    {
    }

    public CompanionAppMessageDto(CompanionAppMessageType messageType)
    {
        MessageType = messageType;
    }
}
