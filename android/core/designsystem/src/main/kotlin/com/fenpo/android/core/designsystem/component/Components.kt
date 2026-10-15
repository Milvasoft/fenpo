package com.fenpo.android.core.designsystem.component

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fenpo.android.core.designsystem.theme.FenpoTheme

/** Rounded card used for grouped content (24 dp corners, card colour). */
@Composable
fun FenpoCard(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(24.dp),
        color = FenpoTheme.extendedColors.card,
    ) {
        Column(content = content)
    }
}

/** Section heading above a group of rows, in the primary colour. */
@Composable
fun SectionLabel(
    text: String,
    modifier: Modifier = Modifier,
) {
    Text(
        text = text,
        modifier = modifier.padding(start = 16.dp, end = 16.dp, top = 22.dp, bottom = 6.dp),
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
    )
}

/**
 * One list or settings row: optional leading content, a title, optional supporting text and an
 * optional trailing slot (switch, button, status).
 */
@Composable
fun FenpoListRow(
    title: String,
    modifier: Modifier = Modifier,
    supporting: String? = null,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable RowScope.() -> Unit)? = null,
) {
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .defaultMinSize(minHeight = if (supporting == null) 56.dp else 72.dp)
                .padding(start = 16.dp, end = 24.dp, top = 10.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        leading?.invoke()
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyLarge,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (supporting != null) {
                Text(
                    text = supporting,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        trailing?.invoke(this)
    }
}

/** A 24 dp line icon in the on-surface-variant colour, used as leading content of a row. */
@Composable
fun LeadingIcon(
    @DrawableRes icon: Int,
    modifier: Modifier = Modifier,
) {
    Icon(
        painter = painterResource(icon),
        contentDescription = null,
        modifier = modifier.size(24.dp),
        tint = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

/** Divider between rows inside a card. */
@Composable
fun RowDivider(modifier: Modifier = Modifier) {
    HorizontalDivider(
        modifier = modifier.padding(horizontal = 16.dp),
        color = MaterialTheme.colorScheme.outlineVariant,
    )
}

/** A small square icon on a coloured background (quick action tiles). */
@Composable
fun IconTile(
    @DrawableRes icon: Int,
    containerColor: Color,
    contentColor: Color,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier =
            modifier
                .size(36.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(containerColor),
        contentAlignment = Alignment.Center,
    ) {
        Icon(painterResource(icon), contentDescription = null, modifier = Modifier.size(20.dp), tint = contentColor)
    }
}

/** File type badge ("PDF", "JPG") shown in transfer lists. */
@Composable
fun FileTypeBadge(
    label: String,
    containerColor: Color,
    contentColor: Color,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier =
            modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(containerColor),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = label, color = contentColor, fontSize = 10.sp, fontWeight = FontWeight.Bold)
    }
}

/**
 * Connection path chip ("Bluetooth", "Wi‑Fi"). Active paths use the success container colours,
 * inactive ones an outline.
 */
@Composable
fun LinkChip(
    label: String,
    @DrawableRes icon: Int,
    active: Boolean,
    modifier: Modifier = Modifier,
) {
    val colors = FenpoTheme.extendedColors
    Surface(
        modifier = modifier.height(32.dp),
        shape = RoundedCornerShape(8.dp),
        color = if (active) colors.successContainer else Color.Transparent,
        contentColor = if (active) colors.onSuccessContainer else MaterialTheme.colorScheme.onSurfaceVariant,
        border = if (active) null else BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
    ) {
        Row(
            modifier = Modifier.padding(start = 8.dp, end = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(painterResource(icon), contentDescription = null, modifier = Modifier.size(18.dp))
            Text(text = label, style = MaterialTheme.typography.labelLarge)
        }
    }
}

/** Highlighted note with an info icon; used for permission hints and platform limits. */
@Composable
fun NoteBox(
    text: String,
    @DrawableRes icon: Int,
    modifier: Modifier = Modifier,
    emphasized: Boolean = true,
) {
    val colors = MaterialTheme.colorScheme
    val container = if (emphasized) colors.primaryContainer else colors.surfaceContainerHigh
    val content = if (emphasized) colors.onPrimaryContainer else colors.onSurface
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(16.dp))
                .background(container)
                .padding(horizontal = 16.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            modifier = Modifier.size(20.dp),
            tint = if (emphasized) content else colors.onSurfaceVariant,
        )
        Text(text = text, style = MaterialTheme.typography.bodyMedium, color = content)
    }
}
