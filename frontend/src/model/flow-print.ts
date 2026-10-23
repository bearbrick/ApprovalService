/**
 * 审批单的打印设置，独立于画布和运行图。
 */
export interface FlowPrintSettings {
  /**
   * A4 纸张方向：portrait 或 landscape。
   */
  orientation: string;
  /**
   * 按顺序打印的审批字段 key。
   */
  fieldKeys: string[];
  /**
   * 是否打印审批意见。
   */
  showComments: boolean;
  /**
   * 是否打印审批人签名。
   */
  showSignatures: boolean;
  /**
   * 是否附票据图片页。
   */
  includeReceiptPages: boolean;
  /**
   * 是否显示单位 Logo。
   */
  showLogo: boolean;
  /**
   * 是否显示二维码。
   */
  showQrCode: boolean;
  /**
   * 是否显示打印人和打印时间水印。
   */
  showWatermark: boolean;
}
