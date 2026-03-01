using UnityEngine;

public interface IDropTarget
{
    // 当拖拽结束并且 DropZone 需要接受该拖拽时调用。
    // 参数说明可以按你项目需要调整：这里示例与前面给出的脚本一致
    // source: 源卡片的拖拽组件（CardDragHandler）
    // dragClone: 正在移动的视觉拷贝（可能为 null）
    // payload: OnDrop 时传入的任意负载（可为匿名对象或自定义类型）
    // 返回 true 表示接受并已处理；false 表示不接受
    bool OnDropAccept(CardDragHandler source, GameObject dragClone, object payload);
}