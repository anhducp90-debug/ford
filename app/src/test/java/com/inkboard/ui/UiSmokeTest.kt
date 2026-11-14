package com.inkboard.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import com.inkboard.MainActivity
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** End-to-end smoke test of the real UI (home → editor → insert → edit → back). */
@OptIn(ExperimentalTestApi::class)
@RunWith(RobolectricTestRunner::class)
@Config(qualifiers = "w800dp-h1280dp-xhdpi")
class UiSmokeTest {
    @get:Rule
    val rule = createAndroidComposeRule<MainActivity>()

    private fun waitText(t: String) = rule.waitUntil(8000) { rule.onAllNodesWithText(t, useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty() }
    private fun waitDesc(t: String) = rule.waitUntil(8000) { rule.onAllNodesWithContentDescription(t).fetchSemanticsNodes().isNotEmpty() }

    @Test
    fun fullFlow() {
        // Onboarding
        waitText("Bỏ qua")
        rule.onNodeWithText("Bỏ qua", useUnmergedTree = true).performClick()

        // New blank document
        rule.onNodeWithText("Mới", useUnmergedTree = true).performClick()
        rule.onNodeWithText("Trống", useUnmergedTree = true).performClick()
        waitDesc("Bút")

        // Pen options popover
        rule.onNodeWithContentDescription("Bút").performClick()
        waitText("Bút nhấn")
        rule.onNodeWithText("Bút nhấn", useUnmergedTree = true).performClick()
        rule.onNodeWithText("Xong", useUnmergedTree = true).performClick()

        // Insert a sticky note -> edit dialog opens
        rule.onNodeWithContentDescription("Chèn").performClick()
        rule.onNodeWithText("Ghi chú dán", useUnmergedTree = true).performClick()
        waitText("Sửa chữ")
        rule.onNode(hasSetTextAction()).performTextInput("Ý tưởng 1")
        rule.onNodeWithText("OK", useUnmergedTree = true).performClick()

        // Context bar is visible for the selection
        waitText("Nhân đôi")
        rule.onNodeWithText("Nhân đôi", useUnmergedTree = true).performClick()

        // Mind map with a branch
        rule.onNodeWithContentDescription("Chèn").performClick()
        rule.onNodeWithText("Mindmap", useUnmergedTree = true).performClick()
        waitText("Nhánh")
        rule.onNodeWithText("Nhánh", useUnmergedTree = true).performClick()
        waitText("Sửa chữ")
        rule.onNodeWithText("OK", useUnmergedTree = true).performClick()

        // Flowchart blocks + table + shapes
        for (item in listOf("Khối quy trình", "Khối quyết định ◇", "Hình tròn ○", "Mũi tên →", "Bảng 3×3")) {
            rule.onNodeWithContentDescription("Chèn").performClick()
            rule.onNodeWithText(item).performClick()
            rule.waitForIdle()
        }
        waitText("Hàng")
        rule.onAllNodesWithText("Hàng", useUnmergedTree = true)[0].performClick()

        // Undo / redo buttons
        rule.onNodeWithContentDescription("Hoàn tác").performClick()
        rule.onNodeWithContentDescription("Làm lại").performClick()

        // Insert text via dialog
        rule.onNodeWithContentDescription("Chèn").performClick()
        rule.onNodeWithText("Chữ", useUnmergedTree = true).performClick()
        waitText("Thêm chữ")
        rule.onNode(hasSetTextAction()).performTextInput("Xin chào InkBoard")
        rule.onNodeWithText("OK", useUnmergedTree = true).performClick()

        // Menu: background + fit
        rule.onNodeWithContentDescription("Menu").performClick()
        rule.onNodeWithText("Nền lưới", useUnmergedTree = true).performClick()
        rule.onNodeWithContentDescription("Về nội dung").performClick()

        // Components panel lists the sticky note; tapping it jumps there and closes the panel
        rule.onNodeWithContentDescription("Thành phần").performClick()
        waitText("Ý tưởng 1")
        rule.onAllNodesWithText("Ý tưởng 1", useUnmergedTree = true)[0].performClick()
        rule.waitForIdle()

        // Toggle rulers and minimap off and on again
        for (item in listOf("Thước toạ độ  ✓", "Bản đồ thu nhỏ  ✓", "Thước toạ độ", "Bản đồ thu nhỏ")) {
            rule.onNodeWithContentDescription("Menu").performClick()
            rule.onNodeWithText(item, useUnmergedTree = true).performClick()
            rule.waitForIdle()
        }

        // Back to home: the document is listed
        rule.onNodeWithContentDescription("Quay lại").performClick()
        rule.waitUntil(10000) { rule.onAllNodes(hasText("Bản vẽ", substring = true)).fetchSemanticsNodes().isNotEmpty() }
        assertTrue(rule.onAllNodes(hasText("Bản vẽ", substring = true)).fetchSemanticsNodes().isNotEmpty())

        // Reopen it
        rule.onNode(hasText("Bản vẽ", substring = true)).performClick()
        waitDesc("Bút")
    }
}
