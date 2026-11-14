package com.inkboard

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import com.inkboard.data.DocMeta
import com.inkboard.data.DocStore
import com.inkboard.ui.EditorScreen
import com.inkboard.ui.HomeScreen
import com.inkboard.ui.Prefs

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val store = DocStore(applicationContext)
        val prefs = Prefs(applicationContext)
        Thread { store.purgeTrash() }.start()
        setContent {
            val dark = isSystemInDarkTheme()
            val scheme = if (dark) darkColorScheme(primary = Color(0xFF8AB4F8), secondary = Color(0xFFFFC857))
            else lightColorScheme(primary = Color(0xFF1F3A5F), secondary = Color(0xFFE0A100))
            MaterialTheme(colorScheme = scheme) {
                var open by remember { mutableStateOf<DocMeta?>(null) }
                val current = open
                if (current == null) {
                    HomeScreen(store, prefs, onOpen = { open = it })
                } else {
                    EditorScreen(store, prefs, current, dark, onClose = { open = null })
                }
            }
        }
    }
}
