export class ConfirmDialogProps {
  icon?: string = 'mdi-alert-circle'
  iconColor?: string = '#F44336'
  cancelText?: string = 'Cancel'
  confirmText?: string = 'Delete'
  cancelColor?: string = '#9E9E9E'
  confirmColor?: string = '#F44336'
  title?: string = 'Confirm Action'
  text?: string = 'Are you sure you want to proceed?'
  maxWidth?: string = '500'
  isCode?: boolean = false
  isException?: boolean = false
  showCancel?: boolean = true
  showConfirm?: boolean = true
  showWarningAlert?: boolean = false
  warningAlertMessage?: string = ''
}
